using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// Direct-file Group statistics for one directory. Descendants are not included.
/// Only directories containing at least one grouped file are represented.
/// </summary>
public sealed record DirectoryRecord(
    FileSystemPath Path,
    int FileCount,
    int GroupedFileCount,
    int GroupCount);
