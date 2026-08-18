using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Decisions;

/// <summary>The precise desired surviving arrangement within a Case boundary.</summary>
public sealed class Disposition
{
    public IReadOnlyList<RequiredPlacement> RequiredPlacements { get; init; } = [];
    public IReadOnlyList<DirectoryMapping> DirectoryMappings { get; init; } = [];
    public IReadOnlySet<FileSystemPath> RemoveEmptyDirectories { get; init; } = new HashSet<FileSystemPath>();
}

public sealed record RequiredPlacement(ContentId Content, FileSystemPath Destination);
