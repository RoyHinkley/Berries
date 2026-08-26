using System.Diagnostics;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// Computes inexpensive branch-local statistics independently of BranchPair enumeration.
/// A branch is a directory plus all of its descendants within the Corpus.
/// </summary>
public sealed class BranchStatisticsAnalyzer(IFileSystem fileSystem)
{
    public BranchStatisticsResult Analyze(
        Corpus corpus,
        Portrait portrait,
        IReadOnlyList<DuplicateSet> duplicateSets,
        DuplicateSettlements settlements,
        IReadOnlyList<DirectoryRecord> duplicateDirectories,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var ancestorsByDirectory = new Dictionary<FileSystemPath, IReadOnlyList<FileSystemPath>>();
        var accumulators = new Dictionary<FileSystemPath, Accumulator>();

        foreach (var group in portrait.Files.GroupBy(file => file.ParentDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileCount = group.Count();

            foreach (var branch in GetAncestorsWithinCorpus(group.Key, corpus, ancestorsByDirectory))
            {
                var accumulator = GetAccumulator(accumulators, branch);
                accumulator.FileCount += fileCount;
                accumulator.DirectoryCount++;
            }
        }

        foreach (var directory in duplicateDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var branch in GetAncestorsWithinCorpus(directory.Path, corpus, ancestorsByDirectory))
            {
                var accumulator = GetAccumulator(accumulators, branch);
                accumulator.DuplicateFileCount += directory.DuplicateFileCount;
                accumulator.DuplicateDirectoryCount++;
            }
        }

        foreach (var duplicateSet in duplicateSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!settlements.HasUnresolvedRelationship(duplicateSet))
                continue;

            var participatingDirectories = GetUnresolvedParticipatingDirectories(duplicateSet, settlements);
            var touchedBranches = new HashSet<FileSystemPath>();

            foreach (var directory in participatingDirectories)
            {
                foreach (var branch in GetAncestorsWithinCorpus(directory, corpus, ancestorsByDirectory))
                    touchedBranches.Add(branch);
            }

            foreach (var branch in touchedBranches)
                GetAccumulator(accumulators, branch).DuplicateContentCount++;
        }

        var branches = accumulators
            .Where(item => item.Value.DuplicateContentCount > 0)
            .Select(item => new BranchRecord(
                item.Key,
                item.Value.FileCount,
                item.Value.DirectoryCount,
                item.Value.DuplicateFileCount,
                item.Value.DuplicateContentCount,
                item.Value.DuplicateDirectoryCount))
            .OrderByDescending(branch => branch.DuplicateContentCount)
            .ThenByDescending(branch => branch.DuplicateFileCount)
            .ThenBy(branch => branch.Path.Value, StringComparer.Ordinal)
            .ToArray();

        timer.Stop();
        return new BranchStatisticsResult(branches, timer.Elapsed);
    }

    private static IReadOnlyCollection<FileSystemPath> GetUnresolvedParticipatingDirectories(
        DuplicateSet duplicateSet,
        DuplicateSettlements settlements)
    {
        if (settlements.AcceptedPairCount == 0)
            return duplicateSet.Files.Select(file => file.ParentDirectory).Distinct().ToArray();

        var directories = new HashSet<FileSystemPath>();
        var files = duplicateSet.Files;

        for (var firstIndex = 0; firstIndex < files.Count - 1; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < files.Count; secondIndex++)
            {
                if (settlements.IsPairAccepted(duplicateSet.Content, files[firstIndex], files[secondIndex]))
                    continue;

                directories.Add(files[firstIndex].ParentDirectory);
                directories.Add(files[secondIndex].ParentDirectory);
            }
        }

        return directories;
    }

    private IReadOnlyList<FileSystemPath> GetAncestorsWithinCorpus(
        FileSystemPath directory,
        Corpus corpus,
        IDictionary<FileSystemPath, IReadOnlyList<FileSystemPath>> cache)
    {
        if (cache.TryGetValue(directory, out var cached))
            return cached;

        var corpusRoot = corpus.Roots
            .Select(root => root.Path)
            .SingleOrDefault(root =>
                fileSystem.PathsEqual(directory, root) || fileSystem.IsDescendant(directory, root));

        if (corpusRoot.Value is null)
            throw new InvalidOperationException($"Directory is outside the corpus: {directory}");

        var ancestors = new List<FileSystemPath>();
        var current = directory;

        while (true)
        {
            ancestors.Add(current);
            if (fileSystem.PathsEqual(current, corpusRoot))
                break;

            current = fileSystem.GetParentDirectory(current)
                ?? throw new InvalidOperationException(
                    $"Could not reach corpus root {corpusRoot} while walking ancestors of {directory}.");
        }

        cache[directory] = ancestors;
        return ancestors;
    }

    private static Accumulator GetAccumulator(
        IDictionary<FileSystemPath, Accumulator> accumulators,
        FileSystemPath path)
    {
        if (!accumulators.TryGetValue(path, out var accumulator))
        {
            accumulator = new Accumulator();
            accumulators[path] = accumulator;
        }

        return accumulator;
    }

    private sealed class Accumulator
    {
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public int DuplicateFileCount { get; set; }
        public int DuplicateContentCount { get; set; }
        public int DuplicateDirectoryCount { get; set; }
    }
}
