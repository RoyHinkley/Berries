using Berries.FileSystem.Abstractions;

namespace Berries.Core;

/// <summary>Platform-neutral progress reported while acquiring an initial portrait.</summary>
public sealed record ScanProgress(
    long FilesExamined,
    long BytesExamined,
    FileSystemPath CurrentPath);
