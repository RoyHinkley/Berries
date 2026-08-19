using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// Direct-file duplicate statistics for one directory. Descendants are not included.
/// Only directories containing at least one duplicated content are represented.
/// </summary>
public sealed record DirectoryRecord(
    FileSystemPath Path,
    int FileCount,
    int DuplicateFileCount,
    int DuplicateContentCount);
