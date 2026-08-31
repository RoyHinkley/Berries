using System.Diagnostics;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>Computes inexpensive Branch-local statistics independently of Branch Pair enumeration.</summary>
public sealed class BranchStatisticsAnalyzer(IFileSystem fileSystem)
{
    public BranchStatisticsResult Analyze(
        Corpus corpus,
        Portrait portrait,
        IReadOnlyList<Group> groups,
        IReadOnlyList<DirectoryRecord> groupedDirectories,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var timer = Stopwatch.StartNew();
        var ancestorsByDirectory = new Dictionary<FileSystemPath, IReadOnlyList<FileSystemPath>>();
        var accumulators = new Dictionary<FileSystemPath, Accumulator>();
        var physicalDirectories = portrait.Files.GroupBy(file => file.ParentDirectory).ToArray();
        var totalWork = (long)physicalDirectories.Length + groupedDirectories.Count + groups.Count;
        long completed = 0;
        progress?.Report(new OperationProgress("Analyzing branches", completed, totalWork));

        foreach (var directoryFiles in physicalDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileCount = directoryFiles.Count();
            foreach (var branch in GetAncestorsWithinCorpus(directoryFiles.Key, corpus, ancestorsByDirectory))
            {
                var accumulator = GetAccumulator(accumulators, branch);
                accumulator.FileCount += fileCount;
                accumulator.DirectoryCount++;
            }
            progress?.Report(new OperationProgress("Analyzing branches", ++completed, totalWork));
        }

        foreach (var directory in groupedDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var branch in GetAncestorsWithinCorpus(directory.Path, corpus, ancestorsByDirectory))
            {
                var accumulator = GetAccumulator(accumulators, branch);
                accumulator.GroupedFileCount += directory.GroupedFileCount;
                accumulator.GroupedDirectoryCount++;
            }
            progress?.Report(new OperationProgress("Analyzing branches", ++completed, totalWork));
        }

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var touchedBranches = new HashSet<FileSystemPath>();
            foreach (var directory in group.Files.Select(file => file.ParentDirectory).Distinct())
            foreach (var branch in GetAncestorsWithinCorpus(directory, corpus, ancestorsByDirectory))
                touchedBranches.Add(branch);

            foreach (var branch in touchedBranches)
                GetAccumulator(accumulators, branch).GroupCount++;

            progress?.Report(new OperationProgress("Analyzing branches", ++completed, totalWork));
        }

        var branches = accumulators
            .Where(item => item.Value.GroupCount > 0)
            .Select(item =>
            {
                var parent = fileSystem.GetParentDirectory(item.Key);
                if (parent is not null && !accumulators.ContainsKey(parent.Value)) parent = null;
                return new BranchRecord(
                    item.Key,
                    parent,
                    item.Value.FileCount,
                    item.Value.DirectoryCount,
                    item.Value.GroupedFileCount,
                    item.Value.GroupCount,
                    item.Value.GroupedDirectoryCount);
            })
            .OrderByDescending(branch => branch.GroupCount)
            .ThenByDescending(branch => branch.GroupedFileCount)
            .ThenBy(branch => branch.Path.Value, StringComparer.Ordinal)
            .ToArray();

        timer.Stop();
        return new BranchStatisticsResult(branches, timer.Elapsed);
    }

    private IReadOnlyList<FileSystemPath> GetAncestorsWithinCorpus(
        FileSystemPath directory,
        Corpus corpus,
        IDictionary<FileSystemPath, IReadOnlyList<FileSystemPath>> cache)
    {
        if (cache.TryGetValue(directory, out var cached)) return cached;

        var corpusRoot = corpus.Roots
            .Select(root => root.Path)
            .SingleOrDefault(root => fileSystem.PathsEqual(directory, root) || fileSystem.IsDescendant(directory, root));
        if (corpusRoot.Value is null)
            throw new InvalidOperationException($"Directory is outside the corpus: {directory}");

        var ancestors = new List<FileSystemPath>();
        var current = directory;
        while (true)
        {
            ancestors.Add(current);
            if (fileSystem.PathsEqual(current, corpusRoot)) break;
            current = fileSystem.GetParentDirectory(current)
                ?? throw new InvalidOperationException($"Could not reach corpus root {corpusRoot} while walking ancestors of {directory}.");
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
        public int GroupedFileCount { get; set; }
        public int GroupCount { get; set; }
        public int GroupedDirectoryCount { get; set; }
    }
}
