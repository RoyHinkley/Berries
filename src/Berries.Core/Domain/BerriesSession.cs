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
    private IReadOnlyList<DuplicateSet> duplicateSets = [];

    public BerriesSession(IFileSystem fileSystem, Portrait initialPortrait)
    {
        this.fileSystem = fileSystem;
        InitialPortrait = initialPortrait;
        WorkingPortrait = initialPortrait;
        Selection = new BerriesSelection(fileSystem, initialPortrait);
        Rebuild();
    }

    public Portrait InitialPortrait { get; }
    public Portrait WorkingPortrait { get; private set; }
    public BerriesSelection Selection { get; }
    public IReadOnlyList<PortraitOperation> Operations => operations;
    public IReadOnlyList<FileAction> Actions => actions;
    public IReadOnlyList<DuplicateSet> DuplicateSets => duplicateSets;

    public int SelectedGroupCount => Selection.CountGroups(DuplicateSets);

    public void InvertSelectedCopies() => Selection.InvertSelectedCopies(DuplicateSets);

    public void Exclude(IEnumerable<FileInstance> files) =>
        AddCommand(DistinctCurrent(files).Select(file =>
            (PortraitOperation)new ExcludePortraitOperation(file.Path)));

    public void Delete(IEnumerable<FileInstance> files) =>
        AddCommand(DistinctCurrent(files).Select(file =>
            (PortraitOperation)new DeletePortraitOperation(file.Path)));

    public MoveResult Move(
        IEnumerable<FileInstance> files,
        FileSystemPath sourceScope,
        FileSystemPath destinationScope)
    {
        var collisions = new List<MoveCollision>();
        var command = new List<PortraitOperation>();
        var selectionMoves = new List<(FileSystemPath Source, FileSystemPath Destination)>();
        var working = WorkingPortrait.Files.ToDictionary(file => PathKey(file.Path), StringComparer.OrdinalIgnoreCase);

        foreach (var requested in DistinctCurrent(files))
        {
            if (!working.TryGetValue(PathKey(requested.Path), out var source)
                || source.Content is null
                || !IsWithinOrEqual(source.ParentDirectory, sourceScope))
                continue;

            var relativeDirectory = fileSystem.GetRelativePath(sourceScope, source.ParentDirectory);
            var destinationDirectory = relativeDirectory.Value == "."
                ? destinationScope
                : fileSystem.Combine(destinationScope, relativeDirectory);

            var existingContent = working.Values.FirstOrDefault(candidate =>
                candidate.Content == source.Content
                && fileSystem.PathsEqual(candidate.ParentDirectory, destinationDirectory)
                && !fileSystem.PathsEqual(candidate.Path, source.Path));
            if (existingContent is not null)
            {
                command.Add(new DeletePortraitOperation(source.Path));
                working.Remove(PathKey(source.Path));
                continue;
            }

            var fileName = fileSystem.GetRelativePath(source.ParentDirectory, source.Path);
            var destinationPath = fileSystem.Combine(destinationDirectory, fileName);
            var destinationKey = PathKey(destinationPath);

            if (working.TryGetValue(destinationKey, out var occupant))
            {
                if (occupant.Content == source.Content)
                {
                    command.Add(new DeletePortraitOperation(source.Path));
                    working.Remove(PathKey(source.Path));
                }
                else
                {
                    collisions.Add(new MoveCollision(source, destinationPath, occupant));
                }
                continue;
            }

            command.Add(new MovePortraitOperation(source.Path, destinationPath));
            selectionMoves.Add((source.Path, destinationPath));
            working.Remove(PathKey(source.Path));
            working[destinationKey] = source with
            {
                Path = destinationPath,
                ParentDirectory = fileSystem.GetParentDirectory(destinationPath)
                    ?? throw new InvalidOperationException($"Destination has no parent: {destinationPath}")
            };
        }

        foreach (var move in selectionMoves) Selection.MovePath(move.Source, move.Destination);
        AddCommand(command);
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
        var current = WorkingPortrait.Files.ToDictionary(file => PathKey(file.Path), StringComparer.OrdinalIgnoreCase);
        var result = new List<FileInstance>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requested in files)
        {
            var key = PathKey(requested.Path);
            if (!seen.Add(key) || !current.TryGetValue(key, out var file))
                continue;
            result.Add(file);
        }

        return result;
    }

    private string PathKey(FileSystemPath path) => fileSystem.NormalizePath(path).Value;

    private bool IsWithinOrEqual(FileSystemPath candidate, FileSystemPath scope) =>
        fileSystem.PathsEqual(candidate, scope) || fileSystem.IsDescendant(candidate, scope);

    private void Rebuild()
    {
        var files = InitialPortrait.Files.ToDictionary(file => PathKey(file.Path), StringComparer.OrdinalIgnoreCase);
        actions.Clear();

        foreach (var operation in operations)
            Apply(operation, files);

        WorkingPortrait = new Portrait(files.Values.ToArray());
        duplicateSets = WorkingPortrait.Files
            .Where(file => file.Content is not null)
            .GroupBy(file => file.Content!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateSet(group.Key, group.ToArray()))
            .ToArray();
        Selection.Refresh(WorkingPortrait);
    }

    private void Apply(PortraitOperation operation, Dictionary<string, FileInstance> files)
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

        var sourceKey = PathKey(source);
        if (!files.TryGetValue(sourceKey, out var file))
            return;

        switch (operation)
        {
            case ExcludePortraitOperation:
                files.Remove(sourceKey);
                break;

            case DeletePortraitOperation:
                files.Remove(sourceKey);
                actions.Add(new DeleteFileAction(file.Path));
                break;

            case MovePortraitOperation move:
                files.Remove(sourceKey);
                files[PathKey(move.Destination)] = file with
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
