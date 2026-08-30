using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Queries;

/// <summary>
/// A duplicate file known to belong to a particular Branch, together with the
/// directory chain from that Branch root to the file's containing directory.
/// </summary>
public sealed record BranchFilePlacement(
    FileInstance File,
    IReadOnlyList<FileSystemPath> Directories);
