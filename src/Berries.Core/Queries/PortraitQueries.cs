using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Queries;

/// <summary>
/// Answers factual questions about the current Berries model. Query implementations may
/// later acquire indexes or other optimizations without changing callers above Core.
/// </summary>
public sealed class PortraitQueries(IFileSystem fileSystem)
{
    public Task<IReadOnlyList<FileInstance>> DuplicateFilesInDirectoryAsync(
        BerriesSession session,
        FileSystemPath directory,
        IProgress<Berries.Core.OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return FindDuplicateFilesAsync(
            session,
            file => fileSystem.PathsEqual(file.ParentDirectory, directory),
            "Finding duplicate files in directory",
            progress,
            cancellationToken);
    }

    public Task<IReadOnlyList<FileInstance>> DuplicateFilesInBranchAsync(
        BerriesSession session,
        FileSystemPath branch,
        IProgress<Berries.Core.OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return FindDuplicateFilesAsync(
            session,
            file => InBranch(file, branch),
            "Finding duplicate files in branch",
            progress,
            cancellationToken);
    }

    public async Task<IReadOnlyList<BranchFilePlacement>> DuplicateFilesInBranchWithPlacementAsync(
        BerriesSession session,
        FileSystemPath branch,
        IProgress<Berries.Core.OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = await DuplicateFilesInBranchAsync(session, branch, progress, cancellationToken);
        var result = new BranchFilePlacement[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[i] = new BranchFilePlacement(files[i], DirectoryChain(branch, files[i].ParentDirectory));
        }
        return result;
    }

    public IReadOnlyList<DuplicateSet> Groups(BerriesSession session) =>
        session.DuplicateSets
            .OrderByDescending(set => set.Files.Count)
            .ThenBy(set => set.Files[0].Path.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<DuplicateSet> GroupsForSelection(BerriesSession session)
    {
        var contents = session.Selection.Files
            .Where(file => file.Content is not null)
            .Select(file => file.Content!.Value)
            .ToHashSet();
        return Groups(session).Where(set => contents.Contains(set.Content)).ToArray();
    }

    public IReadOnlyList<FileInstance> DistinctFiles(IEnumerable<FileInstance> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FileInstance>();
        foreach (var file in files)
        {
            var key = fileSystem.NormalizePath(file.Path).Value;
            if (seen.Add(key)) result.Add(file);
        }
        return result;
    }

    public IReadOnlyList<FileInstance> FilesInContext(
        IEnumerable<FileInstance> files,
        FileSystemPath context,
        bool includeDescendants) =>
        files.Where(file => InContext(file, context, includeDescendants)).ToArray();

    public IReadOnlyList<FileInstance> SelectedFilesInContext(
        BerriesSession session,
        FileSystemPath context,
        bool includeDescendants) =>
        FilesInContext(session.Selection.Files, context, includeDescendants);

    public bool CorpusRootsMatch(Corpus corpus, IEnumerable<FileSystemPath> roots)
    {
        var requested = roots.ToArray();
        if (corpus.Roots.Count != requested.Length) return false;
        return requested.All(root => corpus.Roots.Any(existing => fileSystem.PathsEqual(existing.Path, root)));
    }

    public FileSystemPath? CorpusRootFor(Corpus corpus, FileSystemPath path)
    {
        foreach (var root in corpus.Roots.Select(item => item.Path))
            if (fileSystem.PathsEqual(path, root) || fileSystem.IsDescendant(path, root))
                return root;
        return null;
    }

    public IReadOnlyList<FileSystemPath> AncestorsWithinCorpus(Corpus corpus, FileSystemPath path)
    {
        var root = CorpusRootFor(corpus, path);
        if (root is null) return [];

        var chain = new List<FileSystemPath>();
        var current = path;
        while (true)
        {
            chain.Add(current);
            if (fileSystem.PathsEqual(current, root.Value)) break;
            var parent = fileSystem.GetParentDirectory(current);
            if (parent is null) break;
            current = parent.Value;
        }
        chain.Reverse();
        return chain;
    }

    public DirectoryPair? BestDirectoryPair(
        IReadOnlyList<DirectoryPair> pairs,
        FileSystemPath directory) =>
        pairs
            .Where(pair => fileSystem.PathsEqual(pair.First, directory) || fileSystem.PathsEqual(pair.Second, directory))
            .OrderByDescending(pair => pair.SharedContentCount)
            .FirstOrDefault();

    public bool HasBranchPairCandidate(
        IReadOnlyList<BranchRecord> branches,
        FileSystemPath branch)
    {
        var selected = branches.FirstOrDefault(candidate => fileSystem.PathsEqual(candidate.Path, branch));
        if (selected is null || selected.DuplicateContentCount == 0) return false;

        return branches.Any(candidate =>
            candidate.DuplicateContentCount > 0 &&
            !fileSystem.PathsEqual(candidate.Path, branch) &&
            !fileSystem.IsDescendant(candidate.Path, branch) &&
            !fileSystem.IsDescendant(branch, candidate.Path));
    }

    public Task<int> SharedGroupCountAsync(
        BerriesSession session,
        FileSystemPath first,
        FileSystemPath second,
        bool includeDescendants,
        IProgress<Berries.Core.OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var sets = session.DuplicateSets;
            var count = 0;
            progress?.Report(new Berries.Core.OperationProgress("Counting shared Groups", 0, sets.Count));

            for (var i = 0; i < sets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var inFirst = false;
                var inSecond = false;

                foreach (var file in sets[i].Files)
                {
                    if (!inFirst && InContext(file, first, includeDescendants)) inFirst = true;
                    if (!inSecond && InContext(file, second, includeDescendants)) inSecond = true;
                    if (inFirst && inSecond) break;
                }

                if (inFirst && inSecond) count++;
                if ((i & 0xff) == 0 || i + 1 == sets.Count)
                    progress?.Report(new Berries.Core.OperationProgress("Counting shared Groups", i + 1, sets.Count));
            }

            return count;
        }, cancellationToken);
    }

    private IReadOnlyList<FileSystemPath> DirectoryChain(FileSystemPath branch, FileSystemPath directory)
    {
        if (fileSystem.PathsEqual(branch, directory)) return [];

        var chain = new List<FileSystemPath>();
        var current = directory;
        while (!fileSystem.PathsEqual(current, branch))
        {
            chain.Add(current);
            var parent = fileSystem.GetParentDirectory(current)
                ?? throw new InvalidOperationException($"Could not reach Branch root {branch} while walking ancestors of {directory}.");
            if (!fileSystem.PathsEqual(parent, branch) && !fileSystem.IsDescendant(parent, branch))
                throw new InvalidOperationException($"Directory {directory} is outside Branch {branch}.");
            current = parent;
        }
        chain.Reverse();
        return chain;
    }

    private bool InContext(FileInstance file, FileSystemPath context, bool includeDescendants) =>
        fileSystem.PathsEqual(file.ParentDirectory, context) ||
        (includeDescendants && fileSystem.IsDescendant(file.ParentDirectory, context));

    private bool InBranch(FileInstance file, FileSystemPath branch) => InContext(file, branch, true);

    private Task<IReadOnlyList<FileInstance>> FindDuplicateFilesAsync(
        BerriesSession session,
        Func<FileInstance, bool> includes,
        string progressMessage,
        IProgress<Berries.Core.OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<FileInstance>>(() =>
        {
            var sets = session.DuplicateSets;
            var files = new List<FileInstance>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            progress?.Report(new Berries.Core.OperationProgress(progressMessage, 0, sets.Count));

            for (var i = 0; i < sets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var file in sets[i].Files)
                {
                    var key = fileSystem.NormalizePath(file.Path).Value;
                    if (includes(file) && paths.Add(key)) files.Add(file);
                }

                if ((i & 0xff) == 0 || i + 1 == sets.Count)
                    progress?.Report(new Berries.Core.OperationProgress(progressMessage, i + 1, sets.Count));
            }

            files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path.Value, right.Path.Value));
            return files;
        }, cancellationToken);
    }
}
