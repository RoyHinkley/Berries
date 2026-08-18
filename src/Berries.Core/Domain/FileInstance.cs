using Berries.FileSystem.Abstractions;

namespace Berries.Core.Domain;

/// <summary>A File in PROJECT.md: one filesystem instance at one path.</summary>
public sealed record FileInstance(
    FileSystemPath Path,
    long Length,
    FileSystemPath ParentDirectory,
    ContentId? Content = null,
    DateTimeOffset? LastWriteTime = null);
