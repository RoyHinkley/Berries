namespace Berries.FileSystem.Abstractions;

/// <summary>A platform-neutral regular file exposed by a filesystem adapter.</summary>
public sealed record FileSystemFile(
    FileSystemPath Path,
    FileSystemPath ParentDirectory,
    long Length,
    DateTimeOffset? LastWriteTime = null);
