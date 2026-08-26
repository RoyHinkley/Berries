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
    double Score);

public sealed record BranchCounterpartSeed(
    BranchPriorityMetric Seed,
    IReadOnlyList<BranchCounterpart> Counterparts);

public sealed record BranchCounterpartResult(
    IReadOnlyList<BranchCounterpartSeed> Seeds,
    TimeSpan Elapsed);

/// <summary>
/// Experimental targeted counterpart search. Seeds are considered in branch-priority
/// order. After a seed and its best counterpart are selected, both branches and all
/// their descendants are excluded from later selections. This approximates the effect
/// of completely resolving each selected BranchPair without modifying the portrait.
/// </summary>
public sealed class BranchCounterpartAnalyzer(IFileSystem fileSystem)
{
    public BranchCounterpartResult Analyze(
        Corpus corpus,
        IReadOnlyList<BranchRecord> branches,
        IReadOnlyList<DuplicateSet> duplicateSets,
        DuplicateSettlements settlements,
        int seedLimit = int.MaxValue,
        int counterpartLimit = 1,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var byPath = branches.ToDictionary(branch => branch.Path);
        var rankedSeeds = BranchPriorityMetrics.Calculate(branches)
            .Where(metric => metric.ExcessConcentratedContent > 0)
            .OrderByDescending(metric => metric.ExcessConcentratedContent)
            .ThenByDescending(metric => metric.Branch.DuplicateContentCount)
            .Take(seedLimit)
            .ToArray();

        var ancestorCache = new Dictionary<FileSystemPath, IReadOnlyList<FileSystemPath>>();
        var touchedByDuplicateSet = new List<IReadOnlySet<FileSystemPath>>();

        foreach (var duplicateSet in duplicateSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!settlements.HasUnresolvedRelationship(duplicateSet))
                continue;

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

        var selected = new List<BranchCounterpartSeed>();
        var blockedRoots = new List<FileSystemPath>();

        foreach (var seed in rankedSeeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsBlocked(seed.Branch.Path, blockedRoots))
                continue;

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

            var rankedCandidates = overlaps
                .Where(item => byPath.ContainsKey(item.Key))
                .Select(item => CreateCounterpart(seed, byPath[item.Key], item.Value))
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.SharedDuplicateContentCount)
                .ThenByDescending(item => item.Jaccard)
                .ThenBy(item => item.Branch.Path.Value, StringComparer.Ordinal)
                .ToArray();

            var counterparts = new List<BranchCounterpart>(counterpartLimit);
            foreach (var candidate in rankedCandidates)
            {
                if (counterparts.Any(existing => AreNested(existing.Branch.Path, candidate.Branch.Path)))
                    continue;

                counterparts.Add(candidate);
                if (counterparts.Count == counterpartLimit)
                    break;
            }

            if (counterparts.Count == 0)
                continue;

            selected.Add(new BranchCounterpartSeed(seed, counterparts));

            blockedRoots.Add(seed.Branch.Path);
            blockedRoots.Add(counterparts[0].Branch.Path);
        }

        timer.Stop();
        return new BranchCounterpartResult(selected, timer.Elapsed);
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
        return new BranchCounterpart(
            candidate,
            shared,
            seedCoverage,
            counterpartCoverage,
            jaccard,
            score);
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
