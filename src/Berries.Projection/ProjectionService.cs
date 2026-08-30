using Berries.Core;
using Berries.Core.Domain;
using Berries.Core.Queries;
using Berries.FileSystem.Abstractions;

namespace Berries.Projection;

public sealed class ProjectionService(PortraitQueries queries, IFileSystem fileSystem)
{
    public async Task<DirectoryProjection> DirectoryAsync(
        BerriesSession session,
        FileSystemPath directory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = await queries.DuplicateFilesInDirectoryAsync(session, directory, progress, cancellationToken);
        return new DirectoryProjection(
            directory,
            files.Select(file => new DirectoryProjectionFile(Path.GetFileName(file.Path.Value), file)).ToArray());
    }

    public async Task<BranchProjection> BranchAsync(
        BerriesSession session,
        FileSystemPath branch,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = await queries.DuplicateFilesInBranchAsync(session, branch, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var root = new BranchProjectionNode(branch.Value, branch);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = fileSystem.GetRelativePath(branch, file.Path).Value;
            var parts = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            var currentDirectory = branch;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                currentDirectory = new FileSystemPath(Path.Combine(currentDirectory.Value, parts[i]));
                var directory = currentDirectory;
                var child = current.Children.FirstOrDefault(node =>
                    node.Directory is not null && fileSystem.PathsEqual(node.Directory.Value, directory));
                if (child is null)
                {
                    child = new BranchProjectionNode(parts[i], directory);
                    current.Children.Add(child);
                }
                current = child;
            }

            current.Children.Add(new BranchProjectionNode(
                parts.Length == 0 ? file.Path.Value : parts[^1],
                file: file));
        }

        PopulateFiles(root, cancellationToken);
        return new BranchProjection(branch, root);
    }

    private static IReadOnlyList<FileInstance> PopulateFiles(
        BranchProjectionNode node,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (node.Children.Count == 0) return node.Files;

        var files = new List<FileInstance>();
        foreach (var child in node.Children)
            files.AddRange(PopulateFiles(child, cancellationToken));
        node.Files = files;
        return files;
    }
}
