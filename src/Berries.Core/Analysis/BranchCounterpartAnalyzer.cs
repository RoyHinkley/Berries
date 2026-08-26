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
/// Experimental targeted counterpart search. Starts with intrinsically interesting
/// branches and asks which disjoint branches share their duplicated Content. It does
/// not enumerate BranchPairs.
/// </summary>
public sealed class BranchCounterpartAnalyzer(IFileSystem fileSystem)
{
    public BranchCounterpartResult Analyze(
        Corpus corpus,
        IReadOnlyList<BranchRecord> branches,
        IReadOnlyList<DuplicateSet> duplicateSets,
        DuplicateSettlements settlements,
        int seedLimit = 25,
        int counterpartLimit = 10,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var byPath = branches.ToDictionary(branch => branch.Path);
        var seeds = BranchPriorityMetrics.Calculate(branches)
            .Where(metric => metric.ExcessConcentratedContent > 0)
            .OrderByDescending(metric => metric.ExcessConcentratedContent)
            .ThenByDescending(metric => metric.Branch.DuplicateContentCount)
            .Take(seedLimit)
            .ToArray();
        var seedPaths = seeds.Select(seed => seed.Branch.Path).ToHashSet();
        var overlaps = seeds.ToDictionary(
            seed => seed.Branch.Path,
            _ => new Dictionary<FileSystemPath, int>());
        var ancestorCache = new Dictionary<FileSystemPath, IReadOnlyList<FileSystemPath>>();

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

            foreach (var seedPath in touchedBranches.Where(seedPaths.Contains))
            {
                var counts = overlaps[seedPath];
                foreach (var candidatePath in touchedBranches)
                {
                    if (candidatePath == seedPath || AreNested(seedPath, candidatePath))
                        continue;
                    counts[candidatePath] = counts.GetValueOrDefault(candidatePath) + 1;
                }
            }
        }

        var results = seeds.Select(seed =>
        {
            var rankedCandidates = overlaps[seed.Branch.Path]
                .Where(item => byPath.ContainsKey(item.Key))
                .Select(item =>
                {
                    var candidate = byPath[item.Key];
                    var shared = item.Value;
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
                })
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.SharedDuplicateContentCount)
                .ThenByDescending(item => item.Jaccard)
                .ThenBy(item => item.Branch.Path.Value, StringComparer.Ordinal);

            // Keep independently located counterpart regions. Once a counterpart is
            // selected, its ancestors and descendants describe the same relationship
            // at different boundaries and are suppressed for this seed.
            var counterparts = new List<BranchCounterpart>(counterpartLimit);
            foreach (var candidate in rankedCandidates)
            {
                if (counterparts.Any(selected => AreNested(selected.Branch.Path, candidate.Branch.Path)))
                    continue;

                counterparts.Add(candidate);
                if (counterparts.Count == counterpartLimit)
                    break;
            }

            return new BranchCounterpartSeed(seed, counterparts);
        }).ToArray();

        timer.Stop();
        return new BranchCounterpartResult(results, timer.Elapsed);
    }

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
