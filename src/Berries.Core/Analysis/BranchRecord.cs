using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record BranchRecord(
    FileSystemPath Path,
    FileSystemPath? ParentPath,
    int UniqueFileCount,
    int PortraitFileCount,
    int DirectoryCount,
    int GroupedFileCount,
    int GroupCount,
    int GroupedDirectoryCount)
{
    /// <summary>
    /// Current total file population represented by this Branch: initial unique files
    /// plus files still present in the Working Portrait.
    /// </summary>
    public int FileCount => UniqueFileCount + PortraitFileCount;
}
