using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// Direct-file Group statistics for one directory. Descendants are not included.
/// Only directories containing at least one grouped file are represented.
/// UniqueFileCount is the fixed initial unique population; PortraitFileCount is the
/// current population retained in the Working Portrait.
/// </summary>
public sealed record DirectoryRecord(
    FileSystemPath Path,
    int UniqueFileCount,
    int PortraitFileCount,
    int GroupedFileCount,
    int GroupCount)
{
    public int FileCount => UniqueFileCount + PortraitFileCount;
}
