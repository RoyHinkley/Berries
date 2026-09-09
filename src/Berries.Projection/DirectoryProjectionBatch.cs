using Berries.Core;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Projection;

public static class DirectoryProjectionBatch
{
    public static Task<IReadOnlyList<DirectoryProjection>> BuildAsync(
        BerriesSession session,
        IReadOnlyList<FileSystemPath> directories,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DirectoryProjection>>(() =>
        {
            if (directories.Count == 0)
                return [];

            var directoryIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < directories.Count; i++)
                directoryIndexes[directories[i].Value] = i;

            var buckets = directories.Select(_ => new List<FileInstance>()).ToArray();
            var groups = session.Groups;
            progress?.Report(new OperationProgress("Building Directory views", 0, groups.Count));

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var file in groups[groupIndex].Files)
                    if (directoryIndexes.TryGetValue(file.ParentDirectory.Value, out var directoryIndex))
                        buckets[directoryIndex].Add(file);

                if ((groupIndex & 0xff) == 0 || groupIndex + 1 == groups.Count)
                    progress?.Report(new OperationProgress("Building Directory views", groupIndex + 1, groups.Count));
            }

            var result = new DirectoryProjection[directories.Count];
            for (var i = 0; i < directories.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                buckets[i].Sort((left, right) =>
                    StringComparer.OrdinalIgnoreCase.Compare(left.Path.Value, right.Path.Value));
                result[i] = new DirectoryProjection(
                    directories[i],
                    buckets[i]
                        .Select(file => new DirectoryProjectionFile(Path.GetFileName(file.Path.Value), file))
                        .ToArray());
            }

            return result;
        }, cancellationToken);
    }
}
