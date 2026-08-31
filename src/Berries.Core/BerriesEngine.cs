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

    public Corpus CreateCorpus(IEnumerable<FileSystemPath> selectedRoots)
    {
        var roots = new List<FileSystemPath>();

        foreach (var selectedRoot in selectedRoots)
        {
            var candidate = fileSystem.NormalizePath(selectedRoot);

            if (roots.Any(root => fileSystem.PathsEqual(candidate, root))) continue;
            if (roots.Any(root => fileSystem.IsDescendant(candidate, root))) continue;
            roots.RemoveAll(root => fileSystem.IsDescendant(root, candidate));
            roots.Add(candidate);
        }

        return new Corpus(roots.Select(path => new CorpusRoot(path)));
    }

    public Task<Portrait> BuildInitialPortraitAsync(
        Corpus corpus,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        BuildInitialPortraitAsync(corpus, null, progress, cancellationToken);

    public Task<Portrait> BuildInitialPortraitAsync(
        Corpus corpus,
        Func<FileSystemPath, bool>? excludePath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => BuildInitialPortrait(corpus, excludePath, progress, cancellationToken), cancellationToken);

    public Task<GroupDiscoveryResult> DiscoverGroupsAsync(
        Portrait portrait,
        IProgress<GroupDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DiscoverGroups(portrait, progress, cancellationToken), cancellationToken);

    public Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(
        Portrait portrait,
        IReadOnlyList<Group> groups,
        CancellationToken cancellationToken = default) =>
        AnalyzeDirectoriesAsync(portrait, groups, null, cancellationToken);

    public Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(
        Portrait portrait,
        IReadOnlyList<Group> groups,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => AnalyzeDirectories(portrait, groups, progress, cancellationToken), cancellationToken);

    private Portrait BuildInitialPortrait(
        Corpus corpus,
        Func<FileSystemPath, bool>? excludePath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = new List<FileInstance>();
        long filesExamined = 0;
        long bytesExamined = 0;

        foreach (var root in corpus.Roots)
        foreach (var file in fileSystem.EnumerateFiles(root.Path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (excludePath?.Invoke(file.Path) == true) continue;
            files.Add(new FileInstance(file.Path, file.Length, file.ParentDirectory, LastWriteTime: file.LastWriteTime));
            filesExamined++;
            bytesExamined += file.Length;
            progress?.Report(new ScanProgress(filesExamined, bytesExamined, file.Path));
        }

        return new Portrait(files);
    }

    private GroupDiscoveryResult DiscoverGroups(
        Portrait portrait,
        IProgress<GroupDiscoveryProgress>? progress,
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

        foreach (var candidates in candidateGroups)
        foreach (var file in candidates)
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
                progress?.Report(new GroupDiscoveryProgress(
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
        phaseTimer.Stop();
        var contentHashingElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        var groups = hashedFiles
            .GroupBy(item => item.Content)
            .Where(group => group.Count() > 1)
            .Select(group => new Group(group.Key, group.Select(item => item.File).ToArray()))
            .ToArray();
        phaseTimer.Stop();
        var groupConstructionElapsed = phaseTimer.Elapsed;

        var currentPortrait = evictedFiles.Count == 0
            ? portrait
            : new Portrait(portrait.Files.Where(file => !evictedFiles.Contains(file)));

        totalTimer.Stop();
        return new GroupDiscoveryResult(
            currentPortrait,
            groups,
            evictions,
            new GroupDiscoveryTiming(
                sizeGroupingElapsed,
                contentHashingElapsed,
                groupConstructionElapsed,
                totalTimer.Elapsed));
    }

    private static DirectoryAnalysisResult AnalyzeDirectories(
        Portrait portrait,
        IReadOnlyList<Group> groups,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalTimer = Stopwatch.StartNew();
        var phaseTimer = Stopwatch.StartNew();
        var groupedFilesByDirectory = new Dictionary<FileSystemPath, HashSet<FileSystemPath>>();
        var groupsByDirectory = new Dictionary<FileSystemPath, HashSet<ContentId>>();
        var internalGroupDirectories = new HashSet<FileSystemPath>();
        var pairGroups = new Dictionary<(FileSystemPath First, FileSystemPath Second), HashSet<ContentId>>();
        var totalPairs = groups.Sum(group => (long)group.Files.Count * (group.Files.Count - 1) / 2);
        long examinedPairs = 0;
        progress?.Report(new OperationProgress("Analyzing directories", 0, totalPairs));

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = group.Files;

            for (var firstIndex = 0; firstIndex < files.Count - 1; firstIndex++)
            {
                var first = files[firstIndex];
                for (var secondIndex = firstIndex + 1; secondIndex < files.Count; secondIndex++)
                {
                    var second = files[secondIndex];
                    AddGroupedFile(first, group.Content, groupedFilesByDirectory, groupsByDirectory);
                    AddGroupedFile(second, group.Content, groupedFilesByDirectory, groupsByDirectory);

                    if (first.ParentDirectory == second.ParentDirectory)
                    {
                        internalGroupDirectories.Add(first.ParentDirectory);
                        continue;
                    }

                    var key = CanonicalPair(first.ParentDirectory, second.ParentDirectory);
                    if (!pairGroups.TryGetValue(key, out var sharedGroups))
                    {
                        sharedGroups = [];
                        pairGroups[key] = sharedGroups;
                    }
                    sharedGroups.Add(group.Content);
                }
            }

            examinedPairs += (long)files.Count * (files.Count - 1) / 2;
            progress?.Report(new OperationProgress("Analyzing directories", examinedPairs, totalPairs));
        }

        var directories = portrait.Files
            .GroupBy(file => file.ParentDirectory)
            .Where(group => groupsByDirectory.ContainsKey(group.Key))
            .Select(group => new DirectoryRecord(
                group.Key,
                group.Count(),
                groupedFilesByDirectory[group.Key].Count,
                groupsByDirectory[group.Key].Count))
            .OrderBy(directory => directory.Path.Value, StringComparer.Ordinal)
            .ToArray();
        phaseTimer.Stop();
        var directoryRecordsElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        var directoryPairs = pairGroups
            .Select(pair => new DirectoryPair(pair.Key.First, pair.Key.Second, pair.Value.Count))
            .OrderByDescending(pair => pair.SharedGroupCount)
            .ThenBy(pair => pair.First.Value, StringComparer.Ordinal)
            .ThenBy(pair => pair.Second.Value, StringComparer.Ordinal)
            .ToArray();
        phaseTimer.Stop();
        var directoryPairsElapsed = phaseTimer.Elapsed;

        progress?.Report(new OperationProgress("Finalizing directory analysis"));
        var graph = DirectoryGraphAnalyzer.Analyze(
            portrait,
            directories,
            directoryPairs,
            internalGroupDirectories);
        totalTimer.Stop();

        return new DirectoryAnalysisResult(
            directories,
            directoryPairs,
            graph,
            new DirectoryAnalysisTiming(
                directoryRecordsElapsed,
                directoryPairsElapsed,
                totalTimer.Elapsed));
    }

    private static void AddGroupedFile(
        FileInstance file,
        ContentId content,
        IDictionary<FileSystemPath, HashSet<FileSystemPath>> filesByDirectory,
        IDictionary<FileSystemPath, HashSet<ContentId>> groupsByDirectory)
    {
        if (!filesByDirectory.TryGetValue(file.ParentDirectory, out var files))
        {
            files = [];
            filesByDirectory[file.ParentDirectory] = files;
        }
        files.Add(file.Path);

        if (!groupsByDirectory.TryGetValue(file.ParentDirectory, out var groups))
        {
            groups = [];
            groupsByDirectory[file.ParentDirectory] = groups;
        }
        groups.Add(content);
    }

    private static (FileSystemPath First, FileSystemPath Second) CanonicalPair(
        FileSystemPath first,
        FileSystemPath second) =>
        StringComparer.Ordinal.Compare(first.Value, second.Value) <= 0
            ? (first, second)
            : (second, first);

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
