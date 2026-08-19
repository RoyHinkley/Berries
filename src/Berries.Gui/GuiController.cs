using System.Diagnostics;
using Berries.Core;
using Berries.Core.Cases;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

public sealed class GuiController
{
    private readonly BerriesEngine engine;
    private readonly CaseAnalyzer caseAnalyzer;

    public GuiController(BerriesEngine engine, CaseAnalyzer caseAnalyzer)
    {
        this.engine = engine;
        this.caseAnalyzer = caseAnalyzer;
    }

    public Corpus? Corpus { get; private set; }
    public Portrait? Portrait { get; private set; }
    public ScanResult? Scan { get; private set; }
    public DuplicateDiscoveryResult? DuplicateDiscovery { get; private set; }
    public DirectoryAnalysisResult? DirectoryAnalysis { get; private set; }
    public ScopeAnalysisResult? ScopeAnalysis { get; private set; }
    public CaseAnalysisResult? CaseAnalysis { get; private set; }

    public IReadOnlyList<string> NormalizeRoots(IEnumerable<string> rootPaths) =>
        engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path)))
            .Roots
            .Select(root => root.Path.Value)
            .ToArray();

    public async Task<ScanResult> ScanAsync(
        IEnumerable<string> rootPaths,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var totalTimer = Stopwatch.StartNew();

        var phaseTimer = Stopwatch.StartNew();
        Corpus = engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path)));
        phaseTimer.Stop();
        var normalizationElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        Portrait = await engine.BuildInitialPortraitAsync(Corpus, progress, cancellationToken);
        phaseTimer.Stop();
        var portraitElapsed = phaseTimer.Elapsed;

        DuplicateDiscovery = null;
        DirectoryAnalysis = null;
        ScopeAnalysis = null;
        CaseAnalysis = null;
        totalTimer.Stop();

        Scan = new ScanResult(
            Corpus.Roots.Select(root => root.Path.Value).ToArray(),
            Portrait.Files.Count,
            Portrait.Files.Sum(file => file.Length),
            normalizationElapsed,
            portraitElapsed,
            totalTimer.Elapsed);
        return Scan;
    }

    public async Task<DuplicateDiscoveryResult> DiscoverDuplicatesAsync(
        IProgress<DuplicateDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Portrait is null)
            throw new InvalidOperationException("A portrait must be constructed before duplicate discovery.");

        DuplicateDiscovery = await engine.DiscoverDuplicatesAsync(Portrait, progress, cancellationToken);
        Portrait = DuplicateDiscovery.Portrait;
        DirectoryAnalysis = null;
        ScopeAnalysis = null;
        CaseAnalysis = null;
        return DuplicateDiscovery;
    }

    public async Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(
        CancellationToken cancellationToken = default)
    {
        if (Portrait is null || DuplicateDiscovery is null)
            throw new InvalidOperationException("Duplicate discovery must complete before directory analysis.");

        DirectoryAnalysis = await engine.AnalyzeDirectoriesAsync(
            Portrait,
            DuplicateDiscovery.DuplicateSets,
            cancellationToken);
        ScopeAnalysis = null;
        CaseAnalysis = null;
        return DirectoryAnalysis;
    }

    public async Task<ScopeAnalysisResult> AnalyzeScopesAsync(
        CancellationToken cancellationToken = default)
    {
        if (Corpus is null || DuplicateDiscovery is null)
            throw new InvalidOperationException("Duplicate discovery must complete before scope analysis.");

        ScopeAnalysis = await engine.AnalyzeScopesAsync(
            Corpus,
            DuplicateDiscovery.DuplicateSets,
            cancellationToken);
        CaseAnalysis = null;
        return ScopeAnalysis;
    }

    public CaseAnalysisResult AnalyzeTopCases(int limit = 25)
    {
        if (Portrait is null || DuplicateDiscovery is null || DirectoryAnalysis is null || ScopeAnalysis is null)
            throw new InvalidOperationException("Scope analysis must complete before case analysis.");

        CaseAnalysis = caseAnalyzer.AnalyzeTop(
            Portrait,
            DuplicateDiscovery.DuplicateSets,
            DirectoryAnalysis.DirectoryPairs,
            ScopeAnalysis.ScopePairs,
            limit);
        return CaseAnalysis;
    }
}

public sealed record ScanResult(
    IReadOnlyList<string> Roots,
    int FileCount,
    long TotalBytes,
    TimeSpan CorpusNormalizationElapsed,
    TimeSpan PortraitAcquisitionElapsed,
    TimeSpan TotalElapsed);
