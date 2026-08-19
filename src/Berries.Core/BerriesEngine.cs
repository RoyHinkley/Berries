using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core;

/// <summary>Public entry point for platform- and UI-independent Berries operations.</summary>
public sealed class BerriesEngine
{
    private readonly IFileSystem fileSystem;

    public BerriesEngine(IFileSystem fileSystem) => this.fileSystem = fileSystem;

    /// <summary>
    /// Constructs a corpus from user-selected roots. Exact duplicates and roots contained by
    /// another selected root are removed before any filesystem enumeration occurs.
    /// </summary>
    public Corpus CreateCorpus(IEnumerable<FileSystemPath> selectedRoots)
    {
        var roots = new List<FileSystemPath>();

        foreach (var selectedRoot in selectedRoots)
        {
            var candidate = fileSystem.NormalizePath(selectedRoot);

            if (roots.Any(root => fileSystem.PathsEqual(candidate, root)))
                continue;

            if (roots.Any(root => fileSystem.IsDescendant(candidate, root)))
                continue;

            roots.RemoveAll(root => fileSystem.IsDescendant(root, candidate));
            roots.Add(candidate);
        }

        return new Corpus(roots.Select(path => new CorpusRoot(path)));
    }

    /// <summary>
    /// Acquires filesystem state and constructs an initial portrait without blocking the caller.
    /// Filesystem enumeration itself is synchronous because that is what the platform exposes;
    /// the engine owns the worker-thread boundary so clients do not have to.
    /// </summary>
    public Task<Portrait> BuildInitialPortraitAsync(
        Corpus corpus,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => BuildInitialPortrait(corpus, progress, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Finds byte-identical files in an existing portrait. Files are first grouped by length;
    /// only members of non-singleton length groups are read and hashed. A file that becomes
    /// inaccessible is evicted from the returned portrait and is not considered further.
    /// </summary>
    public Task<DuplicateDiscoveryResult> DiscoverDuplicatesAsync(
        Portrait portrait,
        IProgress<DuplicateDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => DiscoverDuplicates(portrait, progress, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Builds direct-directory duplicate statistics and unordered DirectoryPairs from duplicate sets.
    /// Pair leverage is the number of distinct duplicated contents represented directly in both directories.
    /// </summary>
    public Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(
        Portrait portrait,
        IReadOnlyList<DuplicateSet> duplicateSets,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => AnalyzeDirectories(portrait, duplicateSets, cancellationToken),
            cancellationToken);

    private Portrait BuildInitialPortrait(
        Corpus corpus,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = new List<FileInstance>();
        long filesExamined = 0;
        long bytesExamined = 0;

        foreach (var root in corpus.Roots)
        {
            foreach (var file in fileSystem.EnumerateFiles(root.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                files.Add(new FileInstance(
                    file.Path,
                    file.Length,
                    file.ParentDirectory,
                    LastWriteTime: file.LastWriteTime));

                filesExamined++;
                bytesExamined += file.Length;
                progress?.Report(new ScanProgress(filesExamined, bytesExamined, file.Path));
            }
        }

        return new Portrait(files);
    }

    private DuplicateDiscoveryResult DiscoverDuplicates(
        Portrait portrait,
        IProgress<DuplicateDiscoveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalTimer = Stopwatch.StartNew();

        var phaseTimer = Stopwatch.StartNew();
        var candidateGroups = portrait.Files
            .GroupBy(file => file.Length)
            .Where(group => group.Count() > 1)
            .Select(group => group.ToArray())
            .ToArray();
        phaseTimer.Stop();
        var sizeGroupingElapsed = phaseTimer.Elapsed;

        var candidateFiles = candidateGroups.Sum(group => group.Length);
        var candidateBytes = candidateGroups.Sum(group => group.Sum(file => file.Length));
        long filesHashed = 0;
        long bytesHashed = 0;

        phaseTimer.Restart();
        var hashedFiles = new List<(ContentId Content, FileInstance File)>(candidateFiles);
        var evictions = new List<FileEviction>();
        var evictedFiles = new HashSet<FileInstance>();

        foreach (var group in candidateGroups)
        {
            foreach (var file in group)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryAccessFile(
                    file,
                    "read for content hashing",
                    () =>
                    {
                        using var stream = fileSystem.OpenRead(file.Path);
                        return SHA256.HashData(stream);
                    },
                    evictions,
                    out var hash))
                {
                    var content = new ContentId(Convert.ToHexString(hash));
                    hashedFiles.Add((content, file));
                    filesHashed++;
                    bytesHashed += file.Length;
                    progress?.Report(new DuplicateDiscoveryProgress(
                        filesHashed,
                        candidateFiles,
                        bytesHashed,
                        candidateBytes,
                        file.Path));
                }
                else
                {
                    evictedFiles.Add(file);
                }
            }
        }
        phaseTimer.Stop();
        var contentHashingElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        var duplicateSets = hashedFiles
            .GroupBy(item => item.Content)
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateSet(
                group.Key,
                group.Select(item => item.File).ToArray()))
            .ToArray();
        phaseTimer.Stop();
        var duplicateSetConstructionElapsed = phaseTimer.Elapsed;

        var currentPortrait = evictedFiles.Count == 0
            ? portrait
            : new Portrait(portrait.Files.Where(file => !evictedFiles.Contains(file)));

        totalTimer.Stop();

        return new DuplicateDiscoveryResult(
            currentPortrait,
            duplicateSets,
            evictions,
            new DuplicateDiscoveryTiming(
                sizeGroupingElapsed,
                contentHashingElapsed,
                duplicateSetConstructionElapsed,
                totalTimer.Elapsed));
    }

    private static DirectoryAnalysisResult AnalyzeDirectories(
        Portrait portrait,
        IReadOnlyList<DuplicateSet> duplicateSets,
        CancellationToken cancellationToken)
    {
        var totalTimer = Stopwatch.StartNew();
        var phaseTimer = Stopwatch.StartNew();

        var duplicateFileCounts = new Dictionary<FileSystemPath, int>();
        var duplicateContentsByDirectory = new Dictionary<FileSystemPath, HashSet<ContentId>>();

        foreach (var duplicateSet in duplicateSets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var file in duplicateSet.Files)
            {
                duplicateFileCounts.TryGetValue(file.ParentDirectory, out var count);
                duplicateFileCounts[file.ParentDirectory] = count + 1;

                if (!duplicateContentsByDirectory.TryGetValue(file.ParentDirectory, out var contents))
                {
                    contents = [];
                    duplicateContentsByDirectory[file.ParentDirectory] = contents;
                }

                contents.Add(duplicateSet.Content);
            }
        }

        var directories = portrait.Files
            .GroupBy(file => file.ParentDirectory)
            .Where(group => duplicateContentsByDirectory.ContainsKey(group.Key))
            .Select(group => new DirectoryRecord(
                group.Key,
                group.Count(),
                duplicateFileCounts[group.Key],
                duplicateContentsByDirectory[group.Key].Count))
            .OrderBy(directory => directory.Path.Value, StringComparer.Ordinal)
            .ToArray();

        phaseTimer.Stop();
        var directoryRecordsElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        var pairCounts = new Dictionary<(FileSystemPath First, FileSystemPath Second), int>();

        foreach (var duplicateSet in duplicateSets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var representedDirectories = duplicateSet.Files
                .Select(file => file.ParentDirectory)
                .Distinct()
                .OrderBy(path => path.Value, StringComparer.Ordinal)
                .ToArray();

            for (var firstIndex = 0; firstIndex < representedDirectories.Length - 1; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < representedDirectories.Length; secondIndex++)
                {
                    var key = (representedDirectories[firstIndex], representedDirectories[secondIndex]);
                    pairCounts.TryGetValue(key, out var count);
                    pairCounts[key] = count + 1;
                }
            }
        }

        var directoryPairs = pairCounts
            .Select(pair => new DirectoryPair(pair.Key.First, pair.Key.Second, pair.Value))
            .OrderByDescending(pair => pair.Leverage)
            .ThenBy(pair => pair.First.Value, StringComparer.Ordinal)
            .ThenBy(pair => pair.Second.Value, StringComparer.Ordinal)
            .ToArray();

        phaseTimer.Stop();
        var directoryPairsElapsed = phaseTimer.Elapsed;

        totalTimer.Stop();
        return new DirectoryAnalysisResult(
            directories,
            directoryPairs,
            new DirectoryAnalysisTiming(
                directoryRecordsElapsed,
                directoryPairsElapsed,
                totalTimer.Elapsed));
    }

    private static bool TryAccessFile<T>(
        FileInstance file,
        string operation,
        Func<T> access,
        ICollection<FileEviction> evictions,
        out T result)
    {
        try
        {
            result = access();
            return true;
        }
        catch (Exception ex) when (IsFileAccessFailure(ex))
        {
            evictions.Add(new FileEviction(file, operation, ex.Message));
            result = default!;
            return false;
        }
    }

    private static bool IsFileAccessFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;
}
