using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// Direct-file Group statistics for one directory. Descendants are not included.
/// Only directories containing at least one grouped file are represented.
/// </summary>
public sealed record DirectoryRecord(
    FileSystemPath Path,
    /// <summary>Files in this directory that were unique when initial Group discovery completed.</summary>
    int UniqueFileCount,
    /// <summary>Current files in the Working Portrait, including files no longer in an active Group.</summary>
    int PortraitFileCount,
    int GroupedFileCount,
    int GroupCount)
{
    public int FileCount => UniqueFileCount + PortraitFileCount;
}
