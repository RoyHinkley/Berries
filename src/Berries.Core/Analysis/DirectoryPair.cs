using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>Unordered pair of distinct directories sharing one or more Groups directly.</summary>
public sealed record DirectoryPair(
    FileSystemPath First,
    FileSystemPath Second,
    int SharedGroupCount);
