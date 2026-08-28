using Berries.Core.Analysis;
using Berries.Core.Planning;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Domain;

/// <summary>
/// One Berries working session. The InitialPortrait is fixed; WorkingPortrait and the
/// physical ActionPlan are deterministic products of the ordered portrait operations.
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
        foreach (var file in DistinctCurrent(files))
            operations.Add(new ExcludePortraitOperation(file.Path));
        Rebuild();
    }

    public void Delete(IEnumerable<FileInstance> files)
    {
        foreach (var file in DistinctCurrent(files))
            operations.Add(new DeletePortraitOperation(file.Path));
        Rebuild();
    }

    public MoveResult Move(
        IEnumerable<FileInstance> files,
        FileSystemPath sourceScope,
        FileSystemPath destinationScope)
    {
        var collisions = new List<MoveCollision>();

        foreach (var requested in DistinctCurrent(files))
        {
            var source = FindCurrent(requested.Path);
            if (source?.Content is null)
                continue;
            if (!IsWithinOrEqual(source.ParentDirectory, sourceScope))
                continue;

            var relativeDirectory = fileSystem.GetRelativePath(sourceScope, source.ParentDirectory);
            var destinationDirectory = relativeDirectory.Value == "."
                ? destinationScope
                : fileSystem.Combine(destinationScope, relativeDirectory);

            // Destination organization is authoritative. If this Content is already
            // directly within the intended destination directory, the move has already
            // been accomplished; only the redundant source instance remains.
            var existingContent = WorkingPortrait.Files.FirstOrDefault(candidate =>
                candidate.Content == source.Content
                && fileSystem.PathsEqual(candidate.ParentDirectory, destinationDirectory)
                && !fileSystem.PathsEqual(candidate.Path, source.Path));
            if (existingContent is not null)
            {
                operations.Add(new DeletePortraitOperation(source.Path));
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
                    operations.Add(new DeletePortraitOperation(source.Path));
                    Rebuild();
                }
                else
                {
                    collisions.Add(new MoveCollision(source, destinationPath, occupant));
                }
                continue;
            }

            operations.Add(new MovePortraitOperation(source.Path, destinationPath));
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

    private IReadOnlyList<FileInstance> DistinctCurrent(IEnumerable<FileInstance> files)
    {
        var result = new List<FileInstance>();
        foreach (var requested in files)
        {
            var current = FindCurrent(requested.Path);
            if (current is null)
                continue;
            if (result.Any(existing => fileSystem.PathsEqual(existing.Path, current.Path)))
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
        {
            var index = files.FindIndex(file => fileSystem.PathsEqual(file.Path, operation.Source));
            if (index < 0)
                continue;

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

        WorkingPortrait = new Portrait(files);
    }
}

public abstract record PortraitOperation(FileSystemPath Source);
public sealed record ExcludePortraitOperation(FileSystemPath Source) : PortraitOperation(Source);
public sealed record DeletePortraitOperation(FileSystemPath Source) : PortraitOperation(Source);
public sealed record MovePortraitOperation(FileSystemPath Source, FileSystemPath Destination) : PortraitOperation(Source);

public sealed record MoveCollision(
    FileInstance Source,
    FileSystemPath Destination,
    FileInstance Occupant);

public sealed record MoveResult(IReadOnlyList<MoveCollision> Collisions);
