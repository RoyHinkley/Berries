using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record DirectoryPair(
    FileSystemPath First,
    FileSystemPath Second,
    int SharedContentCount);
