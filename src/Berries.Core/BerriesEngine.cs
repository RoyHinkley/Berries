using System.Diagnostics;
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
    /// only members of non-singleton length groups are read and hashed.
    /// </summary>
    public Task<DuplicateDiscoveryResult> DiscoverDuplicatesAsync(
        Portrait portrait,
        IProgress<DuplicateDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => DiscoverDuplicates(portrait, progress, cancellationToken),
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

        foreach (var group in candidateGroups)
        {
            foreach (var file in group)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var stream = fileSystem.OpenRead(file.Path);
                var hash = SHA256.HashData(stream);
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

        totalTimer.Stop();

        return new DuplicateDiscoveryResult(
            duplicateSets,
            new DuplicateDiscoveryTiming(
                sizeGroupingElapsed,
                contentHashingElapsed,
                duplicateSetConstructionElapsed,
                totalTimer.Elapsed));
    }
}
