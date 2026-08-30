using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Planning;

/// <summary>
/// Executes the concrete filesystem actions compiled from a Working Portrait.
/// Execution is the physical commit boundary; independent failures are reported
/// without abandoning unrelated later actions.
/// </summary>
public sealed class ActionPlanExecutor(IFileSystem fileSystem)
{
    public int CountPhysicalContentLosses(BerriesSession session)
    {
        var physicallyDeleted = session.Actions.OfType<DeleteFileAction>().Select(action => action.Path).ToArray();
        return session.InitialPortrait.Files
            .Where(file => file.Content is not null)
            .GroupBy(file => file.Content!.Value)
            .Count(group => group.All(file => physicallyDeleted.Any(path => fileSystem.PathsEqual(path, file.Path))));
    }

    public Task<ActionPlanExecutionResult> ExecuteAsync(
        IReadOnlyList<FileAction> actions,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = actions.ToArray();
        return Task.Run(() => Execute(snapshot, progress, cancellationToken), cancellationToken);
    }

    private ActionPlanExecutionResult Execute(
        IReadOnlyList<FileAction> actions,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        var skipped = 0;
        var failures = new List<ActionPlanExecutionFailure>();
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
                failures.Add(new ActionPlanExecutionFailure(action, ex.Message));
            }

            progress?.Report(new OperationProgress("Executing filesystem actions", i + 1, actions.Count));
        }

        return new ActionPlanExecutionResult(completed, skipped, failures);
    }

    private void EnsureParent(FileSystemPath destination)
    {
        var parent = fileSystem.GetParentDirectory(destination);
        if (parent is not null && !fileSystem.Exists(parent.Value))
            fileSystem.CreateDirectory(parent.Value);
    }
}

public sealed record ActionPlanExecutionResult(
    int CompletedCount,
    int SkippedCount,
    IReadOnlyList<ActionPlanExecutionFailure> Failures);

public sealed record ActionPlanExecutionFailure(FileAction Action, string Message);
