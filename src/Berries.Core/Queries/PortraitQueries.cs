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
        return Task.Run<IReadOnlyList<FileInstance>>(() =>
        {
            var sets = session.DuplicateSets;
            var files = new List<FileInstance>();
            progress?.Report(new Berries.Core.OperationProgress("Finding duplicate files in directory", 0, sets.Count));

            for (var i = 0; i < sets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var file in sets[i].Files)
                    if (fileSystem.PathsEqual(file.ParentDirectory, directory))
                        files.Add(file);

                if ((i & 0xff) == 0 || i + 1 == sets.Count)
                    progress?.Report(new Berries.Core.OperationProgress("Finding duplicate files in directory", i + 1, sets.Count));
            }

            files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path.Value, right.Path.Value));
            return files;
        }, cancellationToken);
    }
}
