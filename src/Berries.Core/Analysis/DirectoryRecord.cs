using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// Direct-file Group statistics for one directory. Descendants are not included.
/// Only directories containing at least one current Group member are represented.
/// UniqueFileCount is the fixed initial unique population.
/// </summary>
public sealed record DirectoryRecord(
    FileSystemPath Path,
    int UniqueFileCount,
    int GroupedFileCount,
    int GroupCount)
{
    public int FileCount => UniqueFileCount + GroupedFileCount;
}
