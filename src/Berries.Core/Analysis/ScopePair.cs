using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// An unordered pair of effective disjoint directory-rooted scopes supported by descendant
/// DirectoryPair evidence. Leverage is the weighted cut size: each duplicated content contributes
/// once for every contributing DirectoryPair across the cut. This intentionally permits the same
/// content to contribute more than once when it participates through multiple DirectoryPairs.
/// </summary>
public sealed record ScopePair(
    FileSystemPath FirstRoot,
    FileSystemPath SecondRoot,
    int Leverage,
    int DirectoryPairCount);
