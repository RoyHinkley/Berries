using System.Diagnostics;
using Berries.Core;
using Berries.Core.Analysis;
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
    public BranchAnalysisResult? BranchAnalysis { get; private set; }
    public CaseAnalysisResult? CaseAnalysis { get; private set; }
    public DuplicateSettlements DuplicateSettlements { get; } = new();

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

        DuplicateSettlements.Clear();
        DuplicateDiscovery = null;
        DirectoryAnalysis = null;
        BranchAnalysis = null;
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
        BranchAnalysis = null;
        CaseAnalysis = null;
        return DuplicateDiscovery;
    }

    public IReadOnlyList<SprinkledDuplicateCandidate> FindSprinkledDuplicateCandidates(int minimumDirectories = 3)
    {
        if (DuplicateDiscovery is null)
            throw new InvalidOperationException("Duplicate discovery must complete before candidate screening.");
        if (minimumDirectories < 2)
            throw new ArgumentOutOfRangeException(nameof(minimumDirectories));

        return DuplicateDiscovery.DuplicateSets
            .Where(set => !DuplicateSettlements.IsContentAccepted(set.Content))
            .Select(set =>
            {
                var names = set.Files
                    .Select(file => System.IO.Path.GetFileName(file.Path.Value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (names.Length != 1 || string.IsNullOrWhiteSpace(names[0]))
                    return null;

                var directoryCount = set.Files
                    .Select(file => file.ParentDirectory)
                    .Distinct()
                    .Count();

                // Exploratory phenotype: one same-name instance in each represented directory.
                if (directoryCount != set.Files.Count || directoryCount < minimumDirectories)
                    return null;

                return new SprinkledDuplicateCandidate(
                    set,
                    names[0],
                    set.Files.Count,
                    directoryCount);
            })
            .Where(candidate => candidate is not null)
            .Cast<SprinkledDuplicateCandidate>()
            .OrderByDescending(candidate => candidate.DirectoryCount)
            .ThenBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.DuplicateSet.Content.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public void AcceptWholeDuplicateSets(IEnumerable<SprinkledDuplicateCandidate> candidates)
    {
        foreach (var candidate in candidates)
            DuplicateSettlements.Accept(candidate.DuplicateSet);

        DirectoryAnalysis = null;
        BranchAnalysis = null;
        CaseAnalysis = null;
    }

    public async Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(
        CancellationToken cancellationToken = default)
    {
        if (Portrait is null || DuplicateDiscovery is null)
            throw new InvalidOperationException("Duplicate discovery must complete before directory analysis.");

        DirectoryAnalysis = await engine.AnalyzeDirectoriesAsync(
            Portrait,
            DuplicateDiscovery.DuplicateSets,
            DuplicateSettlements,
            cancellationToken);
        BranchAnalysis = null;
        CaseAnalysis = null;
        return DirectoryAnalysis;
    }

    public async Task<BranchAnalysisResult> AnalyzeBranchesAsync(
        CancellationToken cancellationToken = default)
    {
        if (Corpus is null || DirectoryAnalysis is null)
            throw new InvalidOperationException("Directory analysis must complete before branch analysis.");

        BranchAnalysis = await engine.AnalyzeBranchesAsync(
            Corpus,
            DirectoryAnalysis.DirectoryPairs,
            cancellationToken);
        CaseAnalysis = null;
        return BranchAnalysis;
    }

    public CaseAnalysisResult AnalyzeTopCases(int limit = 25)
    {
        if (Portrait is null || DuplicateDiscovery is null || DirectoryAnalysis is null || BranchAnalysis is null)
            throw new InvalidOperationException("Branch analysis must complete before case analysis.");

        CaseAnalysis = caseAnalyzer.AnalyzeTop(
            Portrait,
            DuplicateDiscovery.DuplicateSets,
            DirectoryAnalysis.DirectoryPairs,
            BranchAnalysis.BranchPairs,
            DuplicateSettlements,
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
