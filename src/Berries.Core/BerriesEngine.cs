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

            if (roots.Any(root => fileSystem.PathsEqual(candidate, root)))
                continue;

            if (roots.Any(root => fileSystem.IsDescendant(candidate, root)))
                continue;

            roots.RemoveAll(root => fileSystem.IsDescendant(root, candidate));
            roots.Add(candidate);
        }

        return new Corpus(roots.Select(path => new CorpusRoot(path)));
    }

    public Task<Portrait> BuildInitialPortraitAsync(
        Corpus corpus,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => BuildInitialPortrait(corpus, progress, cancellationToken),
            cancellationToken);

    public Task<DuplicateDiscoveryResult> DiscoverDuplicatesAsync(
        Portrait portrait,
        IProgress<DuplicateDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => DiscoverDuplicates(portrait, progress, cancellationToken),
            cancellationToken);

    public Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(
        Portrait portrait,
        IReadOnlyList<DuplicateSet> duplicateSets,
        CancellationToken cancellationToken = default) =>
        AnalyzeDirectoriesAsync(
            portrait,
            duplicateSets,
            new DuplicateSettlements(),
            cancellationToken);

    public Task<DirectoryAnalysisResult> AnalyzeDirectoriesAsync(
        Portrait portrait,
        IReadOnlyList<DuplicateSet> duplicateSets,
        DuplicateSettlements settlements,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => AnalyzeDirectories(portrait, duplicateSets, settlements, cancellationToken),
            cancellationToken);

    public Task<ScopeAnalysisResult> AnalyzeScopesAsync(
        Corpus corpus,
        IReadOnlyList<DirectoryPair> directoryPairs,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => AnalyzeScopes(corpus, directoryPairs, cancellationToken),
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
        DuplicateSettlements settlements,
        CancellationToken cancellationToken)
    {
        var totalTimer = Stopwatch.StartNew();
        var phaseTimer = Stopwatch.StartNew();

        var unresolvedFilesByDirectory = new Dictionary<FileSystemPath, HashSet<FileSystemPath>>();
        var unresolvedContentsByDirectory = new Dictionary<FileSystemPath, HashSet<ContentId>>();
        var internalDuplicateDirectories = new HashSet<FileSystemPath>();
        var pairContents = new Dictionary<(FileSystemPath First, FileSystemPath Second), HashSet<ContentId>>();

        foreach (var duplicateSet in duplicateSets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (settlements.IsContentAccepted(duplicateSet.Content))
                continue;

            var files = duplicateSet.Files;
            for (var firstIndex = 0; firstIndex < files.Count - 1; firstIndex++)
            {
                var first = files[firstIndex];

                for (var secondIndex = firstIndex + 1; secondIndex < files.Count; secondIndex++)
                {
                    var second = files[secondIndex];
                    if (settlements.IsPairAccepted(duplicateSet.Content, first, second))
                        continue;

                    AddUnresolvedFile(first, duplicateSet.Content, unresolvedFilesByDirectory, unresolvedContentsByDirectory);
                    AddUnresolvedFile(second, duplicateSet.Content, unresolvedFilesByDirectory, unresolvedContentsByDirectory);

                    if (first.ParentDirectory == second.ParentDirectory)
                    {
                        internalDuplicateDirectories.Add(first.ParentDirectory);
                        continue;
                    }

                    var key = CanonicalPair(first.ParentDirectory, second.ParentDirectory);
                    if (!pairContents.TryGetValue(key, out var contents))
                    {
                        contents = [];
                        pairContents[key] = contents;
                    }

                    contents.Add(duplicateSet.Content);
                }
            }
        }

        var directories = portrait.Files
            .GroupBy(file => file.ParentDirectory)
            .Where(group => unresolvedContentsByDirectory.ContainsKey(group.Key))
            .Select(group => new DirectoryRecord(
                group.Key,
                group.Count(),
                unresolvedFilesByDirectory[group.Key].Count,
                unresolvedContentsByDirectory[group.Key].Count))
            .OrderBy(directory => directory.Path.Value, StringComparer.Ordinal)
            .ToArray();

        phaseTimer.Stop();
        var directoryRecordsElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        var directoryPairs = pairContents
            .Select(pair => new DirectoryPair(pair.Key.First, pair.Key.Second, pair.Value.Count))
            .OrderByDescending(pair => pair.Leverage)
            .ThenBy(pair => pair.First.Value, StringComparer.Ordinal)
            .ThenBy(pair => pair.Second.Value, StringComparer.Ordinal)
            .ToArray();
        phaseTimer.Stop();
        var directoryPairsElapsed = phaseTimer.Elapsed;

        var graph = DirectoryGraphAnalyzer.Analyze(
            portrait,
            directories,
            directoryPairs,
            internalDuplicateDirectories);

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

    private static void AddUnresolvedFile(
        FileInstance file,
        ContentId content,
        IDictionary<FileSystemPath, HashSet<FileSystemPath>> filesByDirectory,
        IDictionary<FileSystemPath, HashSet<ContentId>> contentsByDirectory)
    {
        if (!filesByDirectory.TryGetValue(file.ParentDirectory, out var files))
        {
            files = [];
            filesByDirectory[file.ParentDirectory] = files;
        }
        files.Add(file.Path);

        if (!contentsByDirectory.TryGetValue(file.ParentDirectory, out var contents))
        {
            contents = [];
            contentsByDirectory[file.ParentDirectory] = contents;
        }
        contents.Add(content);
    }

    private ScopeAnalysisResult AnalyzeScopes(
        Corpus corpus,
        IReadOnlyList<DirectoryPair> directoryPairs,
        CancellationToken cancellationToken)
    {
        var totalTimer = Stopwatch.StartNew();
        var phaseTimer = Stopwatch.StartNew();
        var evidence = directoryPairs.Where(pair => pair.Leverage > 0).ToArray();
        phaseTimer.Stop();
        var evidenceElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        var ancestorsByDirectory = new Dictionary<FileSystemPath, IReadOnlyList<FileSystemPath>>();
        var accumulators = new Dictionary<(FileSystemPath First, FileSystemPath Second), ScopeAccumulator>();

        foreach (var edge in evidence)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var firstAncestors = GetAncestorsWithinCorpus(edge.First, corpus, ancestorsByDirectory);
            var secondAncestors = GetAncestorsWithinCorpus(edge.Second, corpus, ancestorsByDirectory);
            var directPair = CanonicalPair(edge.First, edge.Second);

            foreach (var firstRoot in firstAncestors)
            {
                foreach (var secondRoot in secondAncestors)
                {
                    if (fileSystem.PathsEqual(firstRoot, secondRoot))
                        continue;

                    var roots = CanonicalPair(firstRoot, secondRoot);
                    if (!CrossesEffectiveSides(edge.First, edge.Second, roots.First, roots.Second))
                        continue;

                    if (!accumulators.TryGetValue(roots, out var accumulator))
                    {
                        accumulator = new ScopeAccumulator();
                        accumulators[roots] = accumulator;
                    }

                    if (accumulator.DirectoryPairs.Add(directPair))
                        accumulator.Leverage += edge.Leverage;
                }
            }
        }

        phaseTimer.Stop();
        var aggregationElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        var scopePairs = accumulators
            .Select(item => new ScopePair(
                item.Key.First,
                item.Key.Second,
                item.Value.Leverage,
                item.Value.DirectoryPairs.Count))
            .OrderByDescending(pair => pair.Leverage)
            .ThenByDescending(pair => pair.DirectoryPairCount)
            .ThenBy(pair => pair.FirstRoot.Value, StringComparer.Ordinal)
            .ThenBy(pair => pair.SecondRoot.Value, StringComparer.Ordinal)
            .ToArray();
        phaseTimer.Stop();
        var resultElapsed = phaseTimer.Elapsed;

        totalTimer.Stop();
        return new ScopeAnalysisResult(
            scopePairs,
            new ScopeAnalysisTiming(
                evidenceElapsed,
                aggregationElapsed,
                resultElapsed,
                totalTimer.Elapsed));
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

    private bool CrossesEffectiveSides(
        FileSystemPath firstDirectory,
        FileSystemPath secondDirectory,
        FileSystemPath firstRoot,
        FileSystemPath secondRoot) =>
        (IsInEffectiveSide(firstDirectory, firstRoot, secondRoot)
            && IsInEffectiveSide(secondDirectory, secondRoot, firstRoot))
        || (IsInEffectiveSide(secondDirectory, firstRoot, secondRoot)
            && IsInEffectiveSide(firstDirectory, secondRoot, firstRoot));

    private bool IsInEffectiveSide(
        FileSystemPath directory,
        FileSystemPath ownRoot,
        FileSystemPath otherRoot)
    {
        if (!Contains(ownRoot, directory))
            return false;

        if (fileSystem.IsDescendant(otherRoot, ownRoot) && Contains(otherRoot, directory))
            return false;

        return true;
    }

    private bool Contains(FileSystemPath root, FileSystemPath path) =>
        fileSystem.PathsEqual(root, path) || fileSystem.IsDescendant(path, root);

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

    private sealed class ScopeAccumulator
    {
        public int Leverage { get; set; }
        public HashSet<(FileSystemPath First, FileSystemPath Second)> DirectoryPairs { get; } = [];
    }
}
