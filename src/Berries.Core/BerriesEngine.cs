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

    public Task<Portrait> BuildInitialPortraitAsync(Corpus corpus, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        BuildInitialPortraitAsync(corpus, null, progress, cancellationToken);

    public Task<Portrait> BuildInitialPortraitAsync(Corpus corpus, Func<FileSystemPath, bool>? ignorePath, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => BuildInitialPortrait(corpus, ignorePath, progress, cancellationToken), cancellationToken);

    public Task<DuplicateDiscoveryResult> DiscoverDuplicatesAsync(Portrait portrait, IProgress<DuplicateDiscoveryProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => DiscoverDuplicates(portrait, progress, cancellationToken), cancellationToken);

    public Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(Portrait portrait, IReadOnlyList<DuplicateSet> duplicateSets, CancellationToken cancellationToken = default) =>
        AnalyzeDirectoriesAsync(portrait, duplicateSets, new DuplicateSettlements(), null, cancellationToken);

    public Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(Portrait portrait, IReadOnlyList<DuplicateSet> duplicateSets, DuplicateSettlements settlements, CancellationToken cancellationToken = default) =>
        AnalyzeDirectoriesAsync(portrait, duplicateSets, settlements, null, cancellationToken);

    public Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(Portrait portrait, IReadOnlyList<DuplicateSet> duplicateSets,
        DuplicateSettlements settlements, IProgress<OperationProgress>? progress, CancellationToken cancellationToken = default) =>
        Task.Run(() => AnalyzeDirectories(portrait, duplicateSets, settlements, progress, cancellationToken), cancellationToken);

    private Portrait BuildInitialPortrait(Corpus corpus, Func<FileSystemPath, bool>? ignorePath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var files = new List<FileInstance>(); long filesExamined = 0; long bytesExamined = 0;
        foreach (var root in corpus.Roots)
        foreach (var file in fileSystem.EnumerateFiles(root.Path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ignorePath?.Invoke(file.Path) == true) continue;
            files.Add(new FileInstance(file.Path, file.Length, file.ParentDirectory, LastWriteTime: file.LastWriteTime));
            filesExamined++; bytesExamined += file.Length;
            progress?.Report(new ScanProgress(filesExamined, bytesExamined, file.Path));
        }
        return new Portrait(files);
    }

    private DuplicateDiscoveryResult DiscoverDuplicates(Portrait portrait, IProgress<DuplicateDiscoveryProgress>? progress, CancellationToken cancellationToken)
    {
        var totalTimer = Stopwatch.StartNew(); var phaseTimer = Stopwatch.StartNew();
        var candidateGroups = portrait.Files.GroupBy(file => file.Length).Where(group => group.Count() > 1).Select(group => group.ToArray()).ToArray();
        phaseTimer.Stop(); var sizeGroupingElapsed = phaseTimer.Elapsed;
        var candidateFiles = candidateGroups.Sum(group => group.Length);
        var candidateBytes = candidateGroups.Sum(group => group.Sum(file => file.Length));
        long filesHashed = 0; long bytesHashed = 0;
        phaseTimer.Restart();
        var hashedFiles = new List<(ContentId Content, FileInstance File)>(candidateFiles);
        var evictions = new List<FileEviction>(); var evictedFiles = new HashSet<FileInstance>();
        foreach (var group in candidateGroups)
        foreach (var file in group)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryAccessFile(file, "read for content hashing", () => { using var stream = fileSystem.OpenRead(file.Path); return SHA256.HashData(stream); }, evictions, out var hash))
            {
                var content = new ContentId(Convert.ToHexString(hash)); hashedFiles.Add((content, file)); filesHashed++; bytesHashed += file.Length;
                progress?.Report(new DuplicateDiscoveryProgress(filesHashed, candidateFiles, bytesHashed, candidateBytes, file.Path));
            }
            else evictedFiles.Add(file);
        }
        phaseTimer.Stop(); var contentHashingElapsed = phaseTimer.Elapsed;
        phaseTimer.Restart();
        var duplicateSets = hashedFiles.GroupBy(item => item.Content).Where(group => group.Count() > 1)
            .Select(group => new DuplicateSet(group.Key, group.Select(item => item.File).ToArray())).ToArray();
        phaseTimer.Stop(); var duplicateSetConstructionElapsed = phaseTimer.Elapsed;
        var currentPortrait = evictedFiles.Count == 0 ? portrait : new Portrait(portrait.Files.Where(file => !evictedFiles.Contains(file)));
        totalTimer.Stop();
        return new DuplicateDiscoveryResult(currentPortrait, duplicateSets, evictions,
            new DuplicateDiscoveryTiming(sizeGroupingElapsed, contentHashingElapsed, duplicateSetConstructionElapsed, totalTimer.Elapsed));
    }

    private static DirectoryAnalysisResult AnalyzeDirectories(Portrait portrait, IReadOnlyList<DuplicateSet> duplicateSets,
        DuplicateSettlements settlements, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var totalTimer = Stopwatch.StartNew(); var phaseTimer = Stopwatch.StartNew();
        var unresolvedFilesByDirectory = new Dictionary<FileSystemPath, HashSet<FileSystemPath>>();
        var unresolvedContentsByDirectory = new Dictionary<FileSystemPath, HashSet<ContentId>>();
        var internalDuplicateDirectories = new HashSet<FileSystemPath>();
        var pairContents = new Dictionary<(FileSystemPath First, FileSystemPath Second), HashSet<ContentId>>();
        var totalPairs = duplicateSets.Sum(set => (long)set.Files.Count * (set.Files.Count - 1) / 2);
        long examinedPairs = 0;
        progress?.Report(new OperationProgress("Analyzing directories", 0, totalPairs));

        foreach (var duplicateSet in duplicateSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = duplicateSet.Files;
            var setPairs = (long)files.Count * (files.Count - 1) / 2;
            if (!settlements.IsContentAccepted(duplicateSet.Content))
            {
                for (var firstIndex = 0; firstIndex < files.Count - 1; firstIndex++)
                {
                    var first = files[firstIndex];
                    for (var secondIndex = firstIndex + 1; secondIndex < files.Count; secondIndex++)
                    {
                        var second = files[secondIndex];
                        if (settlements.IsPairAccepted(duplicateSet.Content, first, second)) continue;
                        AddUnresolvedFile(first, duplicateSet.Content, unresolvedFilesByDirectory, unresolvedContentsByDirectory);
                        AddUnresolvedFile(second, duplicateSet.Content, unresolvedFilesByDirectory, unresolvedContentsByDirectory);
                        if (first.ParentDirectory == second.ParentDirectory) { internalDuplicateDirectories.Add(first.ParentDirectory); continue; }
                        var key = CanonicalPair(first.ParentDirectory, second.ParentDirectory);
                        if (!pairContents.TryGetValue(key, out var contents)) { contents = []; pairContents[key] = contents; }
                        contents.Add(duplicateSet.Content);
                    }
                }
            }
            examinedPairs += setPairs;
            progress?.Report(new OperationProgress("Analyzing directories", examinedPairs, totalPairs));
        }

        var directories = portrait.Files.GroupBy(file => file.ParentDirectory).Where(group => unresolvedContentsByDirectory.ContainsKey(group.Key))
            .Select(group => new DirectoryRecord(group.Key, group.Count(), unresolvedFilesByDirectory[group.Key].Count, unresolvedContentsByDirectory[group.Key].Count))
            .OrderBy(directory => directory.Path.Value, StringComparer.Ordinal).ToArray();
        phaseTimer.Stop(); var directoryRecordsElapsed = phaseTimer.Elapsed;
        phaseTimer.Restart();
        var directoryPairs = pairContents.Select(pair => new DirectoryPair(pair.Key.First, pair.Key.Second, pair.Value.Count))
            .OrderByDescending(pair => pair.Leverage).ThenBy(pair => pair.First.Value, StringComparer.Ordinal).ThenBy(pair => pair.Second.Value, StringComparer.Ordinal).ToArray();
        phaseTimer.Stop(); var directoryPairsElapsed = phaseTimer.Elapsed;
        progress?.Report(new OperationProgress("Finalizing directory analysis"));
        var graph = DirectoryGraphAnalyzer.Analyze(portrait, directories, directoryPairs, internalDuplicateDirectories);
        totalTimer.Stop();
        return new DirectoryAnalysisResult(directories, directoryPairs, graph,
            new DirectoryAnalysisTiming(directoryRecordsElapsed, directoryPairsElapsed, totalTimer.Elapsed));
    }

    private static void AddUnresolvedFile(FileInstance file, ContentId content,
        IDictionary<FileSystemPath, HashSet<FileSystemPath>> filesByDirectory, IDictionary<FileSystemPath, HashSet<ContentId>> contentsByDirectory)
    {
        if (!filesByDirectory.TryGetValue(file.ParentDirectory, out var files)) { files = []; filesByDirectory[file.ParentDirectory] = files; }
        files.Add(file.Path);
        if (!contentsByDirectory.TryGetValue(file.ParentDirectory, out var contents)) { contents = []; contentsByDirectory[file.ParentDirectory] = contents; }
        contents.Add(content);
    }

    private static (FileSystemPath First, FileSystemPath Second) CanonicalPair(FileSystemPath first, FileSystemPath second) =>
        StringComparer.Ordinal.Compare(first.Value, second.Value) <= 0 ? (first, second) : (second, first);

    private static bool TryAccessFile<T>(FileInstance file, string operation, Func<T> access, ICollection<FileEviction> evictions, out T result)
    {
        try { result = access(); return true; }
        catch (Exception ex) when (IsFileAccessFailure(ex)) { evictions.Add(new FileEviction(file, operation, ex.Message)); result = default!; return false; }
    }

    private static bool IsFileAccessFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or SecurityException;
}
