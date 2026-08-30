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

    public DirectoryPair? BestDirectoryPair(
        IReadOnlyList<DirectoryPair> pairs,
        FileSystemPath directory) =>
        pairs
            .Where(pair =>
                fileSystem.PathsEqual(pair.First, directory) ||
                fileSystem.PathsEqual(pair.Second, directory))
            .OrderByDescending(pair => pair.SharedContentCount)
            .FirstOrDefault();

    public bool HasBranchPairCandidate(
        IReadOnlyList<BranchRecord> branches,
        FileSystemPath branch)
    {
        var selected = branches.FirstOrDefault(candidate => fileSystem.PathsEqual(candidate.Path, branch));
        if (selected is null || selected.DuplicateContentCount == 0)
            return false;

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

    private bool InContext(FileInstance file, FileSystemPath context, bool includeDescendants) =>
        fileSystem.PathsEqual(file.ParentDirectory, context) ||
        (includeDescendants && fileSystem.IsDescendant(file.ParentDirectory, context));

    private bool InBranch(FileInstance file, FileSystemPath branch) => InContext(file, branch, true);

    private static Task<IReadOnlyList<FileInstance>> FindDuplicateFilesAsync(
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
            var paths = new HashSet<FileSystemPath>();
            progress?.Report(new Berries.Core.OperationProgress(progressMessage, 0, sets.Count));

            for (var i = 0; i < sets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var file in sets[i].Files)
                    if (includes(file) && paths.Add(file.Path))
                        files.Add(file);

                if ((i & 0xff) == 0 || i + 1 == sets.Count)
                    progress?.Report(new Berries.Core.OperationProgress(progressMessage, i + 1, sets.Count));
            }

            files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path.Value, right.Path.Value));
            return files;
        }, cancellationToken);
    }
}
