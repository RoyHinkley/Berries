using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record DirectoryRecord(
    FileSystemPath Path,
    int ContentCount,
    int DuplicateCount);
