using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Projection;

public sealed record DirectoryProjection(
    FileSystemPath Directory,
    IReadOnlyList<DirectoryProjectionFile> Files);

public sealed record DirectoryProjectionFile(
    string Label,
    FileInstance File);
