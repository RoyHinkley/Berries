using Berries.Core.Analysis;
using Berries.Core.Planning;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Domain;

/// <summary>
/// One Berries working session. The InitialPortrait is fixed; WorkingPortrait and the
/// physical ActionPlan are deterministic products of the ordered portrait operations.
/// One top-level operation represents one user command and is therefore one Undo step.
/// </summary>
public sealed class BerriesSession
{
    private readonly IFileSystem fileSystem;
    private readonly List<PortraitOperation> operations = [];
    private readonly List<FileAction> actions = [];

    public BerriesSession(IFileSystem fileSystem, Portrait initialPortrait)
    {
        this.fileSystem = fileSystem;
        InitialPortrait = initialPortrait;
        WorkingPortrait = initialPortrait;
        Rebuild();
    }

    public Portrait InitialPortrait { get; }
    public Portrait WorkingPortrait { get; private set; }
    public IReadOnlyList<PortraitOperation> Operations => operations;
    public IReadOnlyList<FileAction> Actions => actions;

    public IReadOnlyList<DuplicateSet> DuplicateSets => WorkingPortrait.Files
        .Where(file => file.Content is not null)
        .GroupBy(file => file.Content!.Value)
        .Where(group => group.Count() > 1)
        .Select(group => new DuplicateSet(group.Key, group.ToArray()))
        .ToArray();

    public void Exclude(IEnumerable<FileInstance> files)
    {
        AddCommand(DistinctCurrent(files).Select(file =>
            (PortraitOperation)new ExcludePortraitOperation(file.Path)));
    }

    public void Delete(IEnumerable<FileInstance> files)
    {
        AddCommand(DistinctCurrent(files).Select(file =>
            (PortraitOperation)new DeletePortraitOperation(file.Path)));
    }

    public MoveResult Move(
        IEnumerable<FileInstance> files,
        FileSystemPath sourceScope,
        FileSystemPath destinationScope)
    {
        var collisions = new List<MoveCollision>();
        var command = new List<PortraitOperation>();

        // Resolve the command against a temporary portrait that includes each earlier
        // decision in this same Move command. The command is committed to history only
        // once, so Undo reverses the whole toolbar action.
        var originalOperationCount = operations.Count;
        foreach (var requested in DistinctCurrent(files))
        {
            var source = FindCurrent(requested.Path);
            if (source?.Content is null || !IsWithinOrEqual(source.ParentDirectory, sourceScope))
                continue;

            var relativeDirectory = fileSystem.GetRelativePath(sourceScope, source.ParentDirectory);
            var destinationDirectory = relativeDirectory.Value == "."
                ? destinationScope
                : fileSystem.Combine(destinationScope, relativeDirectory);

            var existingContent = WorkingPortrait.Files.FirstOrDefault(candidate =>
                candidate.Content == source.Content
                && fileSystem.PathsEqual(candidate.ParentDirectory, destinationDirectory)
                && !fileSystem.PathsEqual(candidate.Path, source.Path));
            if (existingContent is not null)
            {
                var operation = new DeletePortraitOperation(source.Path);
                command.Add(operation);
                operations.Add(operation);
                Rebuild();
                continue;
            }

            var fileName = fileSystem.GetRelativePath(source.ParentDirectory, source.Path);
            var destinationPath = fileSystem.Combine(destinationDirectory, fileName);
            var occupant = WorkingPortrait.Files.FirstOrDefault(candidate =>
                fileSystem.PathsEqual(candidate.Path, destinationPath));

            if (occupant is not null)
            {
                if (occupant.Content == source.Content)
                {
                    var operation = new DeletePortraitOperation(source.Path);
                    command.Add(operation);
                    operations.Add(operation);
                    Rebuild();
                }
                else
                {
                    collisions.Add(new MoveCollision(source, destinationPath, occupant));
                }
                continue;
            }

            var move = new MovePortraitOperation(source.Path, destinationPath);
            command.Add(move);
            operations.Add(move);
            Rebuild();
        }

        if (command.Count > 1)
        {
            operations.RemoveRange(originalOperationCount, command.Count);
            operations.Add(new PortraitOperationBatch(command));
            Rebuild();
        }

        return new MoveResult(collisions);
    }

    public bool Undo()
    {
        if (operations.Count == 0)
            return false;
        operations.RemoveAt(operations.Count - 1);
        Rebuild();
        return true;
    }

    private void AddCommand(IEnumerable<PortraitOperation> requested)
    {
        var command = requested.ToArray();
        if (command.Length == 0)
            return;
        operations.Add(command.Length == 1 ? command[0] : new PortraitOperationBatch(command));
        Rebuild();
    }

    private IReadOnlyList<FileInstance> DistinctCurrent(IEnumerable<FileInstance> files)
    {
        var result = new List<FileInstance>();
        foreach (var requested in files)
        {
            var current = FindCurrent(requested.Path);
            if (current is null || result.Any(existing => fileSystem.PathsEqual(existing.Path, current.Path)))
                continue;
            result.Add(current);
        }
        return result;
    }

    private FileInstance? FindCurrent(FileSystemPath path) =>
        WorkingPortrait.Files.FirstOrDefault(file => fileSystem.PathsEqual(file.Path, path));

    private bool IsWithinOrEqual(FileSystemPath candidate, FileSystemPath scope) =>
        fileSystem.PathsEqual(candidate, scope) || fileSystem.IsDescendant(candidate, scope);

    private void Rebuild()
    {
        var files = InitialPortrait.Files.ToList();
        actions.Clear();
        foreach (var operation in operations)
            Apply(operation, files);
        WorkingPortrait = new Portrait(files);
    }

    private void Apply(PortraitOperation operation, List<FileInstance> files)
    {
        if (operation is PortraitOperationBatch batch)
        {
            foreach (var child in batch.Operations)
                Apply(child, files);
            return;
        }

        var source = operation switch
        {
            ExcludePortraitOperation exclude => exclude.Source,
            DeletePortraitOperation delete => delete.Source,
            MovePortraitOperation move => move.Source,
            _ => throw new InvalidOperationException($"Unknown portrait operation: {operation.GetType().Name}")
        };

        var index = files.FindIndex(file => fileSystem.PathsEqual(file.Path, source));
        if (index < 0)
            return;

        var file = files[index];
        switch (operation)
        {
            case ExcludePortraitOperation:
                files.RemoveAt(index);
                break;
            case DeletePortraitOperation:
                files.RemoveAt(index);
                actions.Add(new DeleteFileAction(file.Path));
                break;
            case MovePortraitOperation move:
                files[index] = file with
                {
                    Path = move.Destination,
                    ParentDirectory = fileSystem.GetParentDirectory(move.Destination)
                        ?? throw new InvalidOperationException($"Destination has no parent: {move.Destination}")
                };
                actions.Add(new MoveFileAction(file.Path, move.Destination));
                break;
        }
    }
}

public abstract record PortraitOperation;
public sealed record PortraitOperationBatch(IReadOnlyList<PortraitOperation> Operations) : PortraitOperation;
public sealed record ExcludePortraitOperation(FileSystemPath Source) : PortraitOperation;
public sealed record DeletePortraitOperation(FileSystemPath Source) : PortraitOperation;
public sealed record MovePortraitOperation(FileSystemPath Source, FileSystemPath Destination) : PortraitOperation;

public sealed record MoveCollision(FileInstance Source, FileSystemPath Destination, FileInstance Occupant);
public sealed record MoveResult(IReadOnlyList<MoveCollision> Collisions);
