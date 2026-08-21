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
    public ScopeAnalysisResult? ScopeAnalysis { get; private set; }
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
            DuplicateSettlements,
            cancellationToken);
        ScopeAnalysis = null;
        CaseAnalysis = null;
        return DirectoryAnalysis;
    }

    public async Task<ScopeAnalysisResult> AnalyzeScopesAsync(
        CancellationToken cancellationToken = default)
    {
        if (Corpus is null || DirectoryAnalysis is null)
            throw new InvalidOperationException("Directory analysis must complete before scope analysis.");

        ScopeAnalysis = await engine.AnalyzeScopesAsync(
            Corpus,
            DirectoryAnalysis.DirectoryPairs,
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
            DuplicateSettlements,
            limit);
        return CaseAnalysis;
    }

    public async Task<ProspectiveSettlementComparison?> CompareProspectiveWholeSetSettlementAsync(
        int topCaseLimit = 25,
        CancellationToken cancellationToken = default)
    {
        if (Corpus is null || Portrait is null || DuplicateDiscovery is null
            || DirectoryAnalysis is null || ScopeAnalysis is null || CaseAnalysis is null)
            throw new InvalidOperationException("Complete baseline case analysis before settlement comparison.");

        var candidate = SelectProspectiveWholeSetSettlement();
        if (candidate is null)
            return null;

        var totalTimer = Stopwatch.StartNew();
        var experimentalSettlements = DuplicateSettlements.Copy();
        experimentalSettlements.Accept(candidate.DuplicateSet);

        var settledDirectories = await engine.AnalyzeDirectoriesAsync(
            Portrait,
            DuplicateDiscovery.DuplicateSets,
            experimentalSettlements,
            cancellationToken);

        var settledScopes = await engine.AnalyzeScopesAsync(
            Corpus,
            settledDirectories.DirectoryPairs,
            cancellationToken);

        var settledCases = caseAnalyzer.AnalyzeTop(
            Portrait,
            DuplicateDiscovery.DuplicateSets,
            settledDirectories.DirectoryPairs,
            settledScopes.ScopePairs,
            experimentalSettlements,
            topCaseLimit);

        totalTimer.Stop();

        var baselineKeys = CaseAnalysis.TopCases.Select(CaseIdentity).ToArray();
        var settledKeys = settledCases.TopCases.Select(CaseIdentity).ToArray();
        var settledKeySet = settledKeys.ToHashSet(StringComparer.Ordinal);
        var overlap = baselineKeys.Count(settledKeySet.Contains);
        var sameRank = baselineKeys.Zip(settledKeys).Count(pair => pair.First == pair.Second);

        return new ProspectiveSettlementComparison(
            candidate,
            DirectoryAnalysis,
            ScopeAnalysis,
            CaseAnalysis,
            settledDirectories,
            settledScopes,
            settledCases,
            overlap,
            sameRank,
            totalTimer.Elapsed);
    }

    private ProspectiveSettlementCandidate? SelectProspectiveWholeSetSettlement()
    {
        if (DuplicateDiscovery is null || DirectoryAnalysis is null)
            return null;

        var pairLookup = DirectoryAnalysis.DirectoryPairs.ToDictionary(
            pair => CanonicalPair(pair.First, pair.Second));
        var candidates = new List<ProspectiveSettlementCandidate>();

        foreach (var duplicateSet in DuplicateDiscovery.DuplicateSets)
        {
            if (!DuplicateSettlements.HasUnresolvedRelationship(duplicateSet)
                || duplicateSet.Files.Count < 3)
                continue;

            var names = duplicateSet.Files
                .Select(file => System.IO.Path.GetFileName(file.Path.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (names.Length != 1 || string.IsNullOrEmpty(names[0]))
                continue;

            var directories = duplicateSet.Files
                .Select(file => file.ParentDirectory)
                .Distinct()
                .OrderBy(path => path.Value, StringComparer.Ordinal)
                .ToArray();

            // This exploratory phenotype intentionally targets one same-name instance per directory.
            if (directories.Length != duplicateSet.Files.Count)
                continue;

            long inducedPairs = 0;
            long otherSharedTotal = 0;
            var maxOtherShared = 0;

            for (var firstIndex = 0; firstIndex < directories.Length - 1; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < directories.Length; secondIndex++)
                {
                    inducedPairs++;
                    if (!pairLookup.TryGetValue(
                            CanonicalPair(directories[firstIndex], directories[secondIndex]),
                            out var pair))
                        continue;

                    var otherShared = Math.Max(0, pair.Leverage - 1);
                    otherSharedTotal += otherShared;
                    maxOtherShared = Math.Max(maxOtherShared, otherShared);
                }
            }

            candidates.Add(new ProspectiveSettlementCandidate(
                duplicateSet,
                names[0],
                duplicateSet.Files.Count,
                directories.Length,
                inducedPairs,
                inducedPairs == 0 ? 0 : (double)otherSharedTotal / inducedPairs,
                maxOtherShared));
        }

        return candidates
            .OrderByDescending(candidate => candidate.InducedDirectoryPairCount)
            .ThenBy(candidate => candidate.MeanOtherSharedContent)
            .ThenBy(candidate => candidate.MaxOtherSharedContent)
            .ThenBy(candidate => candidate.CommonFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static (FileSystemPath First, FileSystemPath Second) CanonicalPair(
        FileSystemPath first,
        FileSystemPath second) =>
        StringComparer.Ordinal.Compare(first.Value, second.Value) <= 0
            ? (first, second)
            : (second, first);

    private static string CaseIdentity(Case item) => item switch
    {
        DuplicateSetCase duplicate => "D:" + duplicate.DuplicateSet.Content.Value,
        SingleDirectoryCase directory => "S:" + directory.Directory.Value,
        DirectoryPairCase pair => "P:" + pair.Pair.First.Value + "\n" + pair.Pair.Second.Value,
        ScopePairCase pair => "C:" + pair.Pair.FirstRoot.Value + "\n" + pair.Pair.SecondRoot.Value,
        _ => item.GetType().FullName ?? item.GetType().Name
    };
}

public sealed record ScanResult(
    IReadOnlyList<string> Roots,
    int FileCount,
    long TotalBytes,
    TimeSpan CorpusNormalizationElapsed,
    TimeSpan PortraitAcquisitionElapsed,
    TimeSpan TotalElapsed);
