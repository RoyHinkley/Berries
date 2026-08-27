using System.Diagnostics;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record BranchCounterpart(
    BranchRecord Branch,
    int SharedDuplicateContentCount,
    double SeedCoverage,
    double CounterpartCoverage,
    double Jaccard,
    double Score,
    int DirectDirectoryPairSharedContentCount = 0);

public sealed record BranchCounterpartSeed(
    BranchPriorityMetric Seed,
    IReadOnlyList<BranchCounterpart> Counterparts,
    int CandidateSeedRank = 0);

public sealed record BranchCounterpartResult(
    IReadOnlyList<BranchCounterpartSeed> Seeds,
    TimeSpan Elapsed);

public sealed class BranchCounterpartAnalyzer(IFileSystem fileSystem)
{
    private const int CandidateSeedLimit = 10;

    public BranchCounterpartResult Analyze(
        Corpus corpus,
        IReadOnlyList<BranchRecord> branches,
        IReadOnlyList<DuplicateSet> duplicateSets,
        IReadOnlyList<DirectoryPair> directoryPairs,
        DuplicateSettlements settlements,
        int seedLimit = int.MaxValue,
        int counterpartLimit = 5,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var byPath = branches.ToDictionary(branch => branch.Path);
        var rankedSeeds = BranchPriorityMetrics.Calculate(branches)
            .Where(metric => metric.ExcessConcentratedContent > 0)
            .OrderByDescending(metric => metric.ExcessConcentratedContent)
            .ThenByDescending(metric => metric.Branch.DuplicateContentCount)
            .ToArray();

        var unresolvedSets = duplicateSets
            .Where(set => settlements.HasUnresolvedRelationship(set))
            .ToArray();
        var ancestorCache = new Dictionary<FileSystemPath, IReadOnlyList<FileSystemPath>>();
        var touchedByDuplicateSet = new List<IReadOnlySet<FileSystemPath>>(unresolvedSets.Length);

        foreach (var duplicateSet in unresolvedSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var touchedBranches = new HashSet<FileSystemPath>();
            foreach (var file in duplicateSet.Files)
            {
                foreach (var branch in GetAncestorsWithinCorpus(file.ParentDirectory, corpus, ancestorCache))
                {
                    if (byPath.ContainsKey(branch))
                        touchedBranches.Add(branch);
                }
            }
            touchedByDuplicateSet.Add(touchedBranches);
        }

        var directDirectoryPairSharedContent = new Dictionary<(FileSystemPath, FileSystemPath), int>();
        foreach (var pair in directoryPairs)
        {
            directDirectoryPairSharedContent[(pair.First, pair.Second)] = pair.SharedContentCount;
            directDirectoryPairSharedContent[(pair.Second, pair.First)] = pair.SharedContentCount;
        }

        var selected = new List<BranchCounterpartSeed>();
        var blockedRoots = new List<FileSystemPath>();

        while (selected.Count < seedLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateSeeds = rankedSeeds
                .Where(seed => !IsBlocked(seed.Branch.Path, blockedRoots))
                .Take(CandidateSeedLimit)
                .ToArray();
            if (candidateSeeds.Length == 0)
                break;

            var pairedCandidates = new List<BranchCounterpartSeed>();
            for (var rank = 0; rank < candidateSeeds.Length; rank++)
            {
                var seed = candidateSeeds[rank];
                var counterparts = FindCounterparts(
                    seed,
                    byPath,
                    touchedByDuplicateSet,
                    blockedRoots,
                    counterpartLimit);
                if (counterparts.Count > 0)
                    pairedCandidates.Add(new BranchCounterpartSeed(seed, counterparts, rank + 1));
            }

            if (pairedCandidates.Count == 0)
                break;

            var winner = pairedCandidates
                .OrderByDescending(item => item.Counterparts[0].Score)
                .ThenByDescending(item => item.Seed.ExcessConcentratedContent)
                .ThenBy(item => item.CandidateSeedRank)
                .First();

            var diagnosedCounterparts = winner.Counterparts
                .Select(counterpart => counterpart with
                {
                    DirectDirectoryPairSharedContentCount = directDirectoryPairSharedContent.GetValueOrDefault(
                        (winner.Seed.Branch.Path, counterpart.Branch.Path))
                })
                .ToArray();
            winner = winner with { Counterparts = diagnosedCounterparts };

            selected.Add(winner);
            blockedRoots.Add(winner.Seed.Branch.Path);
            blockedRoots.Add(winner.Counterparts[0].Branch.Path);
        }

        timer.Stop();
        return new BranchCounterpartResult(selected, timer.Elapsed);
    }

    private IReadOnlyList<BranchCounterpart> FindCounterparts(
        BranchPriorityMetric seed,
        IReadOnlyDictionary<FileSystemPath, BranchRecord> byPath,
        IReadOnlyList<IReadOnlySet<FileSystemPath>> touchedByDuplicateSet,
        IReadOnlyList<FileSystemPath> blockedRoots,
        int limit)
    {
        var overlaps = new Dictionary<FileSystemPath, int>();
        foreach (var touchedBranches in touchedByDuplicateSet)
        {
            if (!touchedBranches.Contains(seed.Branch.Path))
                continue;

            foreach (var candidatePath in touchedBranches)
            {
                if (candidatePath == seed.Branch.Path ||
                    AreNested(seed.Branch.Path, candidatePath) ||
                    IsBlocked(candidatePath, blockedRoots))
                    continue;
                overlaps[candidatePath] = overlaps.GetValueOrDefault(candidatePath) + 1;
            }
        }

        return overlaps
            .Where(item => byPath.ContainsKey(item.Key))
            .Select(item => CreateCounterpart(seed, byPath[item.Key], item.Value))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.SharedDuplicateContentCount)
            .ThenByDescending(item => item.Jaccard)
            .ThenBy(item => item.Branch.Path.Value, StringComparer.Ordinal)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private static BranchCounterpart CreateCounterpart(
        BranchPriorityMetric seed,
        BranchRecord candidate,
        int shared)
    {
        var seedCoverage = (double)shared / seed.Branch.DuplicateContentCount;
        var counterpartCoverage = (double)shared / candidate.DuplicateContentCount;
        var union = seed.Branch.DuplicateContentCount + candidate.DuplicateContentCount - shared;
        var jaccard = union == 0 ? 0 : (double)shared / union;
        var score = shared * jaccard;
        return new BranchCounterpart(candidate, shared, seedCoverage, counterpartCoverage, jaccard, score);
    }

    private bool IsBlocked(FileSystemPath path, IReadOnlyList<FileSystemPath> blockedRoots) =>
        blockedRoots.Any(root => path == root || fileSystem.IsDescendant(path, root));

    private bool AreNested(FileSystemPath first, FileSystemPath second) =>
        fileSystem.IsDescendant(first, second) || fileSystem.IsDescendant(second, first);

    private IReadOnlyList<FileSystemPath> GetAncestorsWithinCorpus(
        FileSystemPath directory,
        Corpus corpus,
        IDictionary<FileSystemPath, IReadOnlyList<FileSystemPath>> cache)
    {
        if (cache.TryGetValue(directory, out var cached))
            return cached;

        var corpusRoot = corpus.Roots.Select(root => root.Path).Single(root =>
            fileSystem.PathsEqual(directory, root) || fileSystem.IsDescendant(directory, root));
        var ancestors = new List<FileSystemPath>();
        var current = directory;
        while (true)
        {
            ancestors.Add(current);
            if (fileSystem.PathsEqual(current, corpusRoot))
                break;
            current = fileSystem.GetParentDirectory(current)
                ?? throw new InvalidOperationException($"Could not reach corpus root {corpusRoot} from {directory}.");
        }

        cache[directory] = ancestors;
        return ancestors;
    }
}
