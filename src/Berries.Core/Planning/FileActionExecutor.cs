using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Planning;

/// <summary>
/// Executes concrete filesystem Actions derived from the Working Portrait.
/// Execute is the physical commit boundary; independent failures are reported
/// without abandoning unrelated later Actions.
/// </summary>
public sealed class FileActionExecutor(IFileSystem fileSystem)
{
    public int CountPhysicalContentLosses(BerriesSession session)
    {
        var physicallyDeleted = session.Actions
            .OfType<DeleteFileAction>()
            .Select(action => action.Path)
            .ToArray();

        return session.InitialPortrait.Files
            .Where(file => file.Content is not null)
            .GroupBy(file => file.Content!.Value)
            .Count(group => group.All(file =>
                physicallyDeleted.Any(path => fileSystem.PathsEqual(path, file.Path))));
    }

    public Task<FileActionExecutionResult> ExecuteAsync(
        IReadOnlyList<FileAction> actions,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = actions.ToArray();
        return Task.Run(() => Execute(snapshot, progress, cancellationToken), cancellationToken);
    }

    private FileActionExecutionResult Execute(
        IReadOnlyList<FileAction> actions,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        var skipped = 0;
        var failures = new List<FileActionExecutionFailure>();
        var failedMoveDestinations = new List<FileSystemPath>();
        progress?.Report(new OperationProgress("Executing filesystem actions", 0, actions.Count));

        for (var i = 0; i < actions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = actions[i];

            try
            {
                switch (action)
                {
                    case DeleteFileAction delete:
                        if (failedMoveDestinations.Any(path => fileSystem.PathsEqual(path, delete.Path)))
                        {
                            skipped++;
                            break;
                        }
                        fileSystem.DeleteFile(delete.Path);
                        completed++;
                        break;

                    case CopyFileAction copy:
                        EnsureParent(copy.Destination);
                        fileSystem.CopyFile(copy.Source, copy.Destination);
                        completed++;
                        break;

                    case MoveFileAction move:
                        EnsureParent(move.Destination);
                        try
                        {
                            fileSystem.MoveFile(move.Source, move.Destination);
                        }
                        catch (IOException)
                        {
                            fileSystem.CopyFile(move.Source, move.Destination);
                            fileSystem.DeleteFile(move.Source);
                        }
                        completed++;
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (action is MoveFileAction failedMove)
                    failedMoveDestinations.Add(failedMove.Destination);
                failures.Add(new FileActionExecutionFailure(action, ex.Message));
            }

            progress?.Report(new OperationProgress(
                "Executing filesystem actions",
                i + 1,
                actions.Count));
        }

        return new FileActionExecutionResult(completed, skipped, failures);
    }

    private void EnsureParent(FileSystemPath destination)
    {
        var parent = fileSystem.GetParentDirectory(destination);
        if (parent is not null && !fileSystem.Exists(parent.Value))
            fileSystem.CreateDirectory(parent.Value);
    }
}

public sealed record FileActionExecutionResult(
    int CompletedCount,
    int SkippedCount,
    IReadOnlyList<FileActionExecutionFailure> Failures);

public sealed record FileActionExecutionFailure(FileAction Action, string Message);
