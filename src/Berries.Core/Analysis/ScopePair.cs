using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// An unordered pair of effective disjoint directory-rooted scopes supported by descendant
/// DirectoryPair evidence. Leverage counts distinct duplicated contents crossing the two sides.
/// </summary>
public sealed record ScopePair(
    FileSystemPath FirstRoot,
    FileSystemPath SecondRoot,
    int Leverage,
    int DirectoryPairCount);
