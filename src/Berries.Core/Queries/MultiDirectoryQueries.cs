using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Queries;

public static class MultiDirectoryQueries
{
    public static Task<IReadOnlyList<FileInstance>> FilesInBranchesAsync(
        BerriesSession session,
        IReadOnlyList<FileSystemPath> directories,
        IFileSystem fileSystem,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<FileInstance>>(() =>
        {
            if (directories.Count == 0)
                return [];

            var scopes = directories
                .Select(directory => fileSystem.NormalizePath(directory).Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = session.WorkingPortrait.Files;
            var result = new List<FileInstance>();
            progress?.Report(new OperationProgress("Resolving selected directories", 0, files.Count));

            for (var i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = files[i].ParentDirectory;

                while (true)
                {
                    if (scopes.Contains(fileSystem.NormalizePath(current).Value))
                    {
                        result.Add(files[i]);
                        break;
                    }

                    var parent = fileSystem.GetParentDirectory(current);
                    if (parent is null)
                        break;
                    current = parent.Value;
                }

                if ((i & 0x3ff) == 0 || i + 1 == files.Count)
                    progress?.Report(new OperationProgress("Resolving selected directories", i + 1, files.Count));
            }

            return result;
        }, cancellationToken);
    }
}
