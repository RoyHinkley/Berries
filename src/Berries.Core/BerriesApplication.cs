using System.Diagnostics;
using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core;

/// <summary>
/// Coordinates acquisition, session construction, and derived analysis without presentation concerns.
/// </summary>
public sealed class BerriesApplication
{
    private readonly IFileSystem fileSystem;
    private readonly BerriesEngine engine;
    private readonly BranchStatisticsAnalyzer branchStatisticsAnalyzer;
    private readonly BranchCounterpartAnalyzer branchCounterpartAnalyzer;

    public BerriesApplication(
        IFileSystem fileSystem,
        BerriesEngine engine,
        BranchStatisticsAnalyzer branchStatisticsAnalyzer,
        BranchCounterpartAnalyzer branchCounterpartAnalyzer)
    {
        this.fileSystem = fileSystem;
        this.engine = engine;
        this.branchStatisticsAnalyzer = branchStatisticsAnalyzer;
        this.branchCounterpartAnalyzer = branchCounterpartAnalyzer;
    }

    public event Action<OperationProgress>? AnalysisProgressChanged;

    public Corpus? Corpus { get; private set; }
    public BerriesSession? Session { get; private set; }
    public ScanResult? Scan { get; private set; }
    public DirectoryAnalysisResult? DirectoryAnalysis { get; private set; }
    public BranchStatisticsResult? BranchStatistics { get; private set; }
    public BranchPairSuggestionResult? Suggestions { get; private set; }

    public IReadOnlyList<string> NormalizeRoots(IEnumerable<string> rootPaths) =>
        engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path)))
            .Roots
            .Select(root => root.Path.Value)
            .ToArray();

    public async Task<ScanResult> ScanAsync(
        IEnumerable<string> rootPaths,
        Func<FileSystemPath, bool>? excludePath = null,
        IProgress<ScanProgress>? scanProgress = null,
        IProgress<GroupDiscoveryProgress>? groupProgress = null,
        IProgress<OperationProgress>? analysisProgress = null,
        CancellationToken cancellationToken = default)
    {
        ClearSessionState();

        var totalTimer = Stopwatch.StartNew();
        var phaseTimer = Stopwatch.StartNew();
        Debug.WriteLine("[Berries] Normalizing corpus roots...");
        Corpus = engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path)));
        phaseTimer.Stop();
        var normalizationElapsed = phaseTimer.Elapsed;

        Debug.WriteLine("[Berries] Acquiring initial portrait...");
        phaseTimer.Restart();
        var acquired = await engine.BuildInitialPortraitAsync(
            Corpus,
            excludePath,
            scanProgress,
            cancellationToken);
        phaseTimer.Stop();
        var portraitElapsed = phaseTimer.Elapsed;

        Debug.WriteLine("[Berries] Discovering Groups...");
        var discovery = await engine.DiscoverGroupsAsync(acquired, groupProgress, cancellationToken);
        var contentsByPath = discovery.Groups
            .SelectMany(group => group.Files.Select(file => (file.Path, group.Content)))
            .ToDictionary(item => item.Path, item => item.Content);
        var sessionPortrait = new Portrait(discovery.Portrait.Files.Select(file =>
            contentsByPath.TryGetValue(file.Path, out var content)
                ? file with { Content = content }
                : file));
        Session = new BerriesSession(fileSystem, sessionPortrait);

        totalTimer.Stop();
        Scan = new ScanResult(
            Corpus.Roots.Select(root => root.Path.Value).ToArray(),
            Session.InitialPortrait.Files.Count,
            Session.InitialPortrait.Files.Sum(file => file.Length),
            discovery.Groups.Count,
            discovery.GroupedFileCount,
            normalizationElapsed,
            portraitElapsed,
            discovery.Timing.Total,
            totalTimer.Elapsed,
            discovery.Evictions.Count);

        await RefreshAnalysisAsync(analysisProgress, cancellationToken);
        Debug.WriteLine("[Berries] Scan and analysis ready.");
        return Scan;
    }

    public Task ExcludeAsync(IReadOnlyList<FileInstance> files, CancellationToken cancellationToken = default) =>
        RunSessionCommandAsync(session => session.Exclude(files), cancellationToken);

    public Task DeleteAsync(IReadOnlyList<FileInstance> files, CancellationToken cancellationToken = default) =>
        RunSessionCommandAsync(session => session.Delete(files), cancellationToken);

    public Task<MoveResult> MoveAsync(
        IReadOnlyList<FileInstance> files,
        FileSystemPath source,
        FileSystemPath destination,
        CancellationToken cancellationToken = default) =>
        RunSessionCommandAsync(session => session.Move(files, source, destination), cancellationToken);

    public Task<bool> UndoAsync(CancellationToken cancellationToken = default) =>
        RunSessionCommandAsync(session => session.Undo(), cancellationToken);

    public Task RefreshAnalysisAsync(CancellationToken cancellationToken = default) =>
        RefreshAnalysisAsync(null, cancellationToken);

    public async Task RefreshAnalysisAsync(
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (Corpus is null || Session is null)
            throw new InvalidOperationException("A session must exist before analysis.");

        var corpus = Corpus;
        var session = Session;
        var groups = session.Groups;
        var engineProgress = ForwardProgress(progress);

        Debug.WriteLine("[Berries] Analyzing directories...");
        var directories = await engine.AnalyzeDirectoriesAsync(
            session.WorkingPortrait,
            groups,
            engineProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Debug.WriteLine("[Berries] Analyzing branch statistics...");
        var branches = await Task.Run(() => branchStatisticsAnalyzer.Analyze(
            corpus,
            session.WorkingPortrait,
            groups,
            directories.Directories,
            cancellationToken,
            engineProgress), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Debug.WriteLine("[Berries] Finding Suggestions...");
        var suggestions = await Task.Run(() => branchCounterpartAnalyzer.Analyze(
            corpus,
            branches.Branches,
            groups,
            directories.DirectoryPairs,
            suggestionLimit: 25,
            counterpartLimit: 5,
            cancellationToken: cancellationToken,
            progress: engineProgress), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryAnalysis = directories;
        BranchStatistics = branches;
        Suggestions = suggestions;
        Debug.WriteLine($"[Berries] Analysis ready: {groups.Count:N0} Groups, {suggestions.Suggestions.Count:N0} Suggestions.");
    }

    public Task<BestBranchPairResult?> FindBestBranchPairAsync(
        FileSystemPath branch,
        CancellationToken cancellationToken = default)
    {
        if (Corpus is null || Session is null || BranchStatistics is null)
            return Task.FromResult<BestBranchPairResult?>(null);

        return Task.Run(() => branchCounterpartAnalyzer.FindBestPair(
            Corpus,
            branch,
            BranchStatistics.Branches,
            Session.Groups,
            cancellationToken,
            ForwardProgress(null)), cancellationToken);
    }

    private async Task RunSessionCommandAsync(Action<BerriesSession> command, CancellationToken cancellationToken)
    {
        var session = Session ?? throw new InvalidOperationException("A session must exist before a portrait operation.");
        var operationCount = session.Operations.Count;
        await Task.Run(() => command(session), cancellationToken);
        if (session.Operations.Count != operationCount) InvalidateAnalysis();
    }

    private async Task<T> RunSessionCommandAsync<T>(Func<BerriesSession, T> command, CancellationToken cancellationToken)
    {
        var session = Session ?? throw new InvalidOperationException("A session must exist before a portrait operation.");
        var operationCount = session.Operations.Count;
        var result = await Task.Run(() => command(session), cancellationToken);
        if (session.Operations.Count != operationCount) InvalidateAnalysis();
        return result;
    }

    private void ClearSessionState()
    {
        Corpus = null;
        Session = null;
        Scan = null;
        InvalidateAnalysis();
    }

    private void InvalidateAnalysis()
    {
        DirectoryAnalysis = null;
        BranchStatistics = null;
        Suggestions = null;
    }

    private IProgress<OperationProgress> ForwardProgress(IProgress<OperationProgress>? progress) =>
        new CallbackProgress<OperationProgress>(value =>
        {
            progress?.Report(value);
            AnalysisProgressChanged?.Invoke(value);
        });

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}

public sealed record ScanResult(
    IReadOnlyList<string> Roots,
    int FileCount,
    long TotalBytes,
    int GroupCount,
    int GroupedFileCount,
    TimeSpan CorpusNormalizationElapsed,
    TimeSpan PortraitAcquisitionElapsed,
    TimeSpan GroupDiscoveryElapsed,
    TimeSpan TotalElapsed,
    int EvictionCount);
