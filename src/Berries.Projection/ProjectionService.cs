using Berries.Core;
using Berries.Core.Domain;
using Berries.Core.Queries;
using Berries.FileSystem.Abstractions;

namespace Berries.Projection;

public sealed class ProjectionService(PortraitQueries queries)
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
}
