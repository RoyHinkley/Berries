using System.Diagnostics;
using Berries.Core;
using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

public sealed class GuiController
{
    private readonly IFileSystem fileSystem;
    private readonly BerriesEngine engine;
    private readonly BranchStatisticsAnalyzer branchStatisticsAnalyzer;
    private readonly BranchCounterpartAnalyzer counterpartAnalyzer;

    public GuiController(IFileSystem fileSystem, BerriesEngine engine, BranchStatisticsAnalyzer branchStatisticsAnalyzer, BranchCounterpartAnalyzer counterpartAnalyzer)
    {
        this.fileSystem = fileSystem;
        this.engine = engine;
        this.branchStatisticsAnalyzer = branchStatisticsAnalyzer;
        this.counterpartAnalyzer = counterpartAnalyzer;
    }

    public Corpus? Corpus { get; private set; }
    public BerriesSession? Session { get; private set; }
    public ScanResult? Scan { get; private set; }
    public DirectoryAnalysisResult? DirectoryAnalysis { get; private set; }
    public BranchStatisticsResult? BranchStatistics { get; private set; }
    public BranchCounterpartResult? Counterparts { get; private set; }

    public IReadOnlyList<string> NormalizeRoots(IEnumerable<string> rootPaths) =>
        engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path))).Roots.Select(root => root.Path.Value).ToArray();

    public async Task<ScanResult> ScanAsync(IEnumerable<string> rootPaths, Func<FileSystemPath, bool>? excludePath = null,
        IProgress<ScanProgress>? scanProgress = null, IProgress<DuplicateDiscoveryProgress>? duplicateProgress = null,
        CancellationToken cancellationToken = default)
    {
        var totalTimer = Stopwatch.StartNew(); var phaseTimer = Stopwatch.StartNew();
        Debug.WriteLine("[Berries] Normalizing corpus roots...");
        Corpus = engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path)));
        phaseTimer.Stop(); var normalizationElapsed = phaseTimer.Elapsed;

        Debug.WriteLine("[Berries] Acquiring initial portrait...");
        phaseTimer.Restart();
        var acquired = await engine.BuildInitialPortraitAsync(Corpus, excludePath, scanProgress, cancellationToken);
        phaseTimer.Stop(); var portraitElapsed = phaseTimer.Elapsed;

        Debug.WriteLine("[Berries] Discovering duplicate content...");
        var duplicates = await engine.DiscoverDuplicatesAsync(acquired, duplicateProgress, cancellationToken);
        var contentsByPath = duplicates.DuplicateSets.SelectMany(set => set.Files.Select(file => (file.Path, set.Content)))
            .ToDictionary(item => item.Path, item => item.Content);
        var sessionPortrait = new Portrait(duplicates.Portrait.Files.Select(file =>
            contentsByPath.TryGetValue(file.Path, out var content) ? file with { Content = content } : file));
        Session = new BerriesSession(fileSystem, sessionPortrait);

        totalTimer.Stop();
        Scan = new ScanResult(Corpus.Roots.Select(root => root.Path.Value).ToArray(), Session.InitialPortrait.Files.Count,
            Session.InitialPortrait.Files.Sum(file => file.Length), duplicates.DuplicateSets.Count, duplicates.DuplicateFileCount,
            normalizationElapsed, portraitElapsed, duplicates.Timing.Total, totalTimer.Elapsed, duplicates.Evictions.Count);

        await RefreshAnalysisAsync(cancellationToken);
        Debug.WriteLine("[Berries] Scan and analysis ready.");
        return Scan;
    }

    public async Task RefreshAnalysisAsync(CancellationToken cancellationToken = default)
    {
        if (Corpus is null || Session is null)
            throw new InvalidOperationException("A session must exist before analysis.");

        var corpus = Corpus;
        var session = Session;
        var settlements = new DuplicateSettlements();
        var duplicateSets = session.DuplicateSets;

        Debug.WriteLine("[Berries] Analyzing directories...");
        var directories = await engine.AnalyzeDirectoriesAsync(session.WorkingPortrait, duplicateSets, settlements, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Debug.WriteLine("[Berries] Analyzing branch statistics...");
        var branches = await Task.Run(() => branchStatisticsAnalyzer.Analyze(corpus, session.WorkingPortrait, duplicateSets,
            settlements, directories.Directories, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Debug.WriteLine("[Berries] Finding branch counterparts...");
        var counterparts = await Task.Run(() => counterpartAnalyzer.Analyze(corpus, branches.Branches, duplicateSets,
            directories.DirectoryPairs, settlements, seedLimit: 25, counterpartLimit: 5, cancellationToken: cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Publish a complete generation together. A cancelled background refresh must not
        // leave directory, branch, and suggestion data from different portrait versions.
        DirectoryAnalysis = directories;
        BranchStatistics = branches;
        Counterparts = counterparts;
        Debug.WriteLine($"[Berries] Analysis ready: {duplicateSets.Count:N0} duplicate Contents, {counterparts.Seeds.Count:N0} suggested branch seeds.");
    }
}

public sealed record ScanResult(IReadOnlyList<string> Roots, int FileCount, long TotalBytes, int DuplicateSetCount,
    int DuplicateFileCount, TimeSpan CorpusNormalizationElapsed, TimeSpan PortraitAcquisitionElapsed,
    TimeSpan DuplicateDiscoveryElapsed, TimeSpan TotalElapsed, int EvictionCount);
