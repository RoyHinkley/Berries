using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record BranchRecord(
    FileSystemPath Path,
    FileSystemPath? ParentPath,
    int FileCount,
    int DirectoryCount,
    int GroupedFileCount,
    int GroupCount,
    int GroupedDirectoryCount);
