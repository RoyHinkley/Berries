using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// Unordered pair of distinct directories sharing one or more distinct duplicated contents.
/// SharedContentCount is the DirectoryPair's leverage.
/// </summary>
public sealed record DirectoryPair(
    FileSystemPath First,
    FileSystemPath Second,
    int SharedContentCount)
{
    public int Leverage => SharedContentCount;
}
