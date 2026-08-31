using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record BranchRecord(
    FileSystemPath Path,
    FileSystemPath? ParentPath,
    int UniqueFileCount,
    int DirectoryCount,
    int GroupedFileCount,
    int GroupCount,
    int GroupedDirectoryCount)
{
    /// <summary>
    /// Current total file population represented by this Branch: initial unique files
    /// plus current members of Groups discovered during the primary scan.
    /// </summary>
    public int FileCount => UniqueFileCount + GroupedFileCount;
}
