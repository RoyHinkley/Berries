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
    private readonly BranchCounterpartAnalyzer counterpartAnalyzer;

    public BerriesApplication(IFileSystem fileSystem, BerriesEngine engine,
        BranchStatisticsAnalyzer branchStatisticsAnalyzer, BranchCounterpartAnalyzer counterpartAnalyzer)
    {
        this.fileSystem = fileSystem;
        this.engine = engine;
        this.branchStatisticsAnalyzer = branchStatisticsAnalyzer;
        this.counterpartAnalyzer = counterpartAnalyzer;
    }

    public event Action<OperationProgress>? AnalysisProgressChanged;

    public Corpus? Corpus { get; private set; }
    public BerriesSession? Session { get; private set; }
    public ScanResult? Scan { get; private set; }
    public DirectoryAnalysisResult? DirectoryAnalysis { get; private set; }
    public BranchStatisticsResult? BranchStatistics { get; private set; }
    public BranchCounterpartResult? Counterparts { get; private set; }

    public IReadOnlyList<string> NormalizeRoots(IEnumerable<string> rootPaths) =>
        engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path))).Roots.Select(root => root.Path.Value).ToArray();

    public async Task<ScanResult> ScanAsync(IEnumerable<string> rootPaths,
        Func<FileSystemPath, bool>? excludePath = null,
        IProgress<ScanProgress>? scanProgress = null,
        IProgress<DuplicateDiscoveryProgress>? duplicateProgress = null,
        IProgress<OperationProgress>? analysisProgress = null,
        CancellationToken cancellationToken = default)
    {
        var totalTimer = Stopwatch.StartNew();
        var phaseTimer = Stopwatch.StartNew();
        Debug.WriteLine("[Berries] Normalizing corpus roots...");
        Corpus = engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path)));
        phaseTimer.Stop();
        var normalizationElapsed = phaseTimer.Elapsed;

        Debug.WriteLine("[Berries] Acquiring initial portrait...");
        phaseTimer.Restart();
        var acquired = await engine.BuildInitialPortraitAsync(Corpus, excludePath, scanProgress, cancellationToken);
        phaseTimer.Stop();
        var portraitElapsed = phaseTimer.Elapsed;

        Debug.WriteLine("[Berries] Discovering duplicate content...");
        var duplicates = await engine.DiscoverDuplicatesAsync(acquired, duplicateProgress, cancellationToken);
        var contentsByPath = duplicates.DuplicateSets
            .SelectMany(set => set.Files.Select(file => (file.Path, set.Content)))
            .ToDictionary(item => item.Path, item => item.Content);
        var sessionPortrait = new Portrait(duplicates.Portrait.Files.Select(file =>
            contentsByPath.TryGetValue(file.Path, out var content) ? file with { Content = content } : file));
        Session = new BerriesSession(fileSystem, sessionPortrait);

        totalTimer.Stop();
        Scan = new ScanResult(
            Corpus.Roots.Select(root => root.Path.Value).ToArray(),
            Session.InitialPortrait.Files.Count,
            Session.InitialPortrait.Files.Sum(file => file.Length),
            duplicates.DuplicateSets.Count,
            duplicates.DuplicateFileCount,
            normalizationElapsed,
            portraitElapsed,
            duplicates.Timing.Total,
            totalTimer.Elapsed,
            duplicates.Evictions.Count);

        await RefreshAnalysisAsync(analysisProgress, cancellationToken);
        Debug.WriteLine("[Berries] Scan and analysis ready.");
        return Scan;
    }

    public Task RefreshAnalysisAsync(CancellationToken cancellationToken = default) =>
        RefreshAnalysisAsync(null, cancellationToken);

    public async Task RefreshAnalysisAsync(IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (Corpus is null || Session is null)
            throw new InvalidOperationException("A session must exist before analysis.");

        var corpus = Corpus;
        var session = Session;
        var settlements = new DuplicateSettlements();
        var duplicateSets = session.DuplicateSets;
        var engineProgress = ForwardProgress(progress);

        Debug.WriteLine("[Berries] Analyzing directories...");
        var directories = await engine.AnalyzeDirectoriesAsync(
            session.WorkingPortrait, duplicateSets, settlements, engineProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Debug.WriteLine("[Berries] Analyzing branch statistics...");
        var branches = await Task.Run(() => branchStatisticsAnalyzer.Analyze(
            corpus, session.WorkingPortrait, duplicateSets, settlements, directories.Directories,
            cancellationToken, engineProgress), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Debug.WriteLine("[Berries] Finding branch counterparts...");
        var counterparts = await Task.Run(() => counterpartAnalyzer.Analyze(
            corpus, branches.Branches, duplicateSets, directories.DirectoryPairs, settlements,
            seedLimit: 25, counterpartLimit: 5, cancellationToken: cancellationToken, progress: engineProgress), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryAnalysis = directories;
        BranchStatistics = branches;
        Counterparts = counterparts;
        Debug.WriteLine($"[Berries] Analysis ready: {duplicateSets.Count:N0} duplicate Contents, {counterparts.Seeds.Count:N0} suggested branch seeds.");
    }

    public Task<BestBranchPairResult?> FindBestBranchPairAsync(FileSystemPath branch,
        CancellationToken cancellationToken = default)
    {
        if (Corpus is null || Session is null || BranchStatistics is null)
            return Task.FromResult<BestBranchPairResult?>(null);

        var corpus = Corpus;
        var duplicateSets = Session.DuplicateSets;
        var branches = BranchStatistics.Branches;
        var progress = ForwardProgress(null);
        return Task.Run(() => counterpartAnalyzer.FindBestPair(
            corpus, branch, branches, duplicateSets, cancellationToken, progress), cancellationToken);
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
    int DuplicateSetCount,
    int DuplicateFileCount,
    TimeSpan CorpusNormalizationElapsed,
    TimeSpan PortraitAcquisitionElapsed,
    TimeSpan DuplicateDiscoveryElapsed,
    TimeSpan TotalElapsed,
    int EvictionCount);
