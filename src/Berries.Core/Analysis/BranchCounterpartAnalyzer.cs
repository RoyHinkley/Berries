using System.Diagnostics;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record BranchCounterpart(
    BranchRecord Branch,
    int SharedGroupCount,
    double SeedCoverage,
    double CounterpartCoverage,
    double Jaccard,
    double Score,
    int DirectDirectoryPairSharedGroupCount = 0);

/// <summary>
/// A Branch Pair selected for presentation because its relationship appears worth user attention.
/// The Seed is an internal search aid; its highest-scoring Counterpart forms the suggested pair.
/// </summary>
public sealed record BranchPairSuggestion(
    BranchPriorityMetric Seed,
    IReadOnlyList<BranchCounterpart> Counterparts,
    int CandidateSeedRank = 0);

public sealed record BranchPairSuggestionResult(
    IReadOnlyList<BranchPairSuggestion> Suggestions,
    TimeSpan Elapsed);

public sealed record BestBranchPairResult(
    FileSystemPath First,
    FileSystemPath Second,
    int SharedGroupCount,
    double Score);

public sealed class BranchCounterpartAnalyzer(IFileSystem fileSystem)
{
    // Evaluate several strong seeds before selecting a Suggestion. The winning Branch Pair
    // is chosen by pair score and therefore need not originate from the highest-ranked seed.
    private const int CandidateSeedLimit = 10;

    public BranchPairSuggestionResult Analyze(
        Corpus corpus,
        IReadOnlyList<BranchRecord> branches,
        IReadOnlyList<Group> groups,
        IReadOnlyList<DirectoryPair> directoryPairs,
        int suggestionLimit = int.MaxValue,
        int counterpartLimit = 5,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var timer = Stopwatch.StartNew();
        var byPath = branches.ToDictionary(branch => branch.Path);
        var rankedSeeds = BranchPriorityMetrics.Calculate(branches)
            .Where(metric => metric.ExcessConcentratedGroups > 0)
            .OrderByDescending(metric => metric.ExcessConcentratedGroups)
            .ThenByDescending(metric => metric.Branch.GroupCount)
            .ToArray();

        var ancestorCache = new Dictionary<FileSystemPath, IReadOnlyList<FileSystemPath>>();
        var touchedByGroup = new List<IReadOnlySet<FileSystemPath>>(groups.Count);
        progress?.Report(new OperationProgress("Indexing Group relationships", 0, groups.Count));
        long indexed = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var touchedBranches = new HashSet<FileSystemPath>();
            foreach (var file in group.Files)
            foreach (var branch in GetAncestorsWithinCorpus(file.ParentDirectory, corpus, ancestorCache))
                if (byPath.ContainsKey(branch)) touchedBranches.Add(branch);
            touchedByGroup.Add(touchedBranches);
            progress?.Report(new OperationProgress("Indexing Group relationships", ++indexed, groups.Count));
        }

        var directDirectoryPairSharedGroups = new Dictionary<(FileSystemPath, FileSystemPath), int>();
        foreach (var pair in directoryPairs)
        {
            directDirectoryPairSharedGroups[(pair.First, pair.Second)] = pair.SharedGroupCount;
            directDirectoryPairSharedGroups[(pair.Second, pair.First)] = pair.SharedGroupCount;
        }

        progress?.Report(new OperationProgress("Finding Suggestions"));
        var suggestions = new List<BranchPairSuggestion>();
        var blockedRoots = new List<FileSystemPath>();

        while (suggestions.Count < suggestionLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateSeeds = rankedSeeds
                .Where(seed => !IsBlocked(seed.Branch.Path, blockedRoots))
                .Take(CandidateSeedLimit)
                .ToArray();
            if (candidateSeeds.Length == 0) break;

            var pairCandidates = new List<BranchPairSuggestion>();
            for (var rank = 0; rank < candidateSeeds.Length; rank++)
            {
                var seed = candidateSeeds[rank];
                var counterparts = FindCounterparts(
                    seed,
                    byPath,
                    touchedByGroup,
                    blockedRoots,
                    counterpartLimit);
                if (counterparts.Count > 0)
                    pairCandidates.Add(new BranchPairSuggestion(seed, counterparts, rank + 1));
            }

            if (pairCandidates.Count == 0) break;

            // Select the strongest pair among the candidate seeds. Seed rank breaks ties only;
            // a lower-ranked seed can therefore yield the best Suggestion.
            var winner = pairCandidates
                .OrderByDescending(item => item.Counterparts[0].Score)
                .ThenByDescending(item => item.Seed.ExcessConcentratedGroups)
                .ThenBy(item => item.CandidateSeedRank)
                .First();

            var diagnosedCounterparts = winner.Counterparts.Select(counterpart => counterpart with
            {
                DirectDirectoryPairSharedGroupCount = directDirectoryPairSharedGroups.GetValueOrDefault(
                    (winner.Seed.Branch.Path, counterpart.Branch.Path))
            }).ToArray();

            winner = winner with { Counterparts = diagnosedCounterparts };
            suggestions.Add(winner);
            blockedRoots.Add(winner.Seed.Branch.Path);
            blockedRoots.Add(winner.Counterparts[0].Branch.Path);
            progress?.Report(new OperationProgress($"Finding Suggestions — {suggestions.Count:N0} found"));
        }

        timer.Stop();
        return new BranchPairSuggestionResult(suggestions, timer.Elapsed);
    }

    public BestBranchPairResult? FindBestPair(
        Corpus corpus,
        FileSystemPath branch,
        IReadOnlyList<BranchRecord> branches,
        IReadOnlyList<Group> groups,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var seed = branches.FirstOrDefault(candidate => fileSystem.PathsEqual(candidate.Path, branch));
        if (seed is null || seed.GroupCount == 0) return null;

        var seedContents = ContentsUnder(branch, groups);
        if (seedContents.Count == 0) return null;

        var candidates = branches.Where(candidate =>
                !fileSystem.PathsEqual(candidate.Path, branch)
                && !fileSystem.IsDescendant(candidate.Path, branch)
                && !fileSystem.IsDescendant(branch, candidate.Path)
                && candidate.GroupCount > 0)
            .ToArray();

        progress?.Report(new OperationProgress("Finding best Branch Pair", 0, candidates.Length));
        BestBranchPairResult? best = null;
        double bestScore = 0;
        long completed = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateContents = ContentsUnder(candidate.Path, groups);
            var shared = seedContents.Count(content => candidateContents.Contains(content));
            if (shared > 0)
            {
                var union = seedContents.Count + candidateContents.Count - shared;
                var score = union == 0 ? 0 : shared * ((double)shared / union);
                if (best is null || score > bestScore)
                {
                    bestScore = score;
                    best = new BestBranchPairResult(branch, candidate.Path, shared, score);
                }
            }
            progress?.Report(new OperationProgress("Finding best Branch Pair", ++completed, candidates.Length));
        }

        return best;
    }

    private HashSet<ContentId> ContentsUnder(FileSystemPath branch, IReadOnlyList<Group> groups) =>
        groups
            .Where(group => group.Files.Any(file =>
                fileSystem.PathsEqual(file.ParentDirectory, branch)
                || fileSystem.IsDescendant(file.ParentDirectory, branch)))
            .Select(group => group.Content)
            .ToHashSet();

    private IReadOnlyList<BranchCounterpart> FindCounterparts(
        BranchPriorityMetric seed,
        IReadOnlyDictionary<FileSystemPath, BranchRecord> byPath,
        IReadOnlyList<IReadOnlySet<FileSystemPath>> touchedByGroup,
        IReadOnlyList<FileSystemPath> blockedRoots,
        int limit)
    {
        var overlaps = new Dictionary<FileSystemPath, int>();
        foreach (var touchedBranches in touchedByGroup)
        {
            if (!touchedBranches.Contains(seed.Branch.Path)) continue;
            foreach (var candidatePath in touchedBranches)
            {
                if (candidatePath == seed.Branch.Path
                    || AreNested(seed.Branch.Path, candidatePath)
                    || IsBlocked(candidatePath, blockedRoots))
                    continue;
                overlaps[candidatePath] = overlaps.GetValueOrDefault(candidatePath) + 1;
            }
        }

        return overlaps
            .Where(item => byPath.ContainsKey(item.Key))
            .Select(item => CreateCounterpart(seed, byPath[item.Key], item.Value))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.SharedGroupCount)
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
        var seedCoverage = (double)shared / seed.Branch.GroupCount;
        var counterpartCoverage = (double)shared / candidate.GroupCount;
        var union = seed.Branch.GroupCount + candidate.GroupCount - shared;
        var jaccard = union == 0 ? 0 : (double)shared / union;
        return new BranchCounterpart(
            candidate,
            shared,
            seedCoverage,
            counterpartCoverage,
            jaccard,
            shared * jaccard);
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
        if (cache.TryGetValue(directory, out var cached)) return cached;
        var corpusRoot = corpus.Roots
            .Select(root => root.Path)
            .Single(root => fileSystem.PathsEqual(directory, root) || fileSystem.IsDescendant(directory, root));

        var ancestors = new List<FileSystemPath>();
        var current = directory;
        while (true)
        {
            ancestors.Add(current);
            if (fileSystem.PathsEqual(current, corpusRoot)) break;
            current = fileSystem.GetParentDirectory(current)
                ?? throw new InvalidOperationException($"Could not reach corpus root {corpusRoot} from {directory}.");
        }

        cache[directory] = ancestors;
        return ancestors;
    }
}
