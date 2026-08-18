namespace Berries.Core.Analysis;

/// <summary>Derived duplicate/structural indexes for one Portrait.</summary>
public sealed record PortraitAnalysis(
    IReadOnlyList<DuplicateSet> DuplicateSets,
    IReadOnlyList<DirectoryRecord> Directories,
    IReadOnlyList<DirectoryPair> DirectoryPairs,
    IReadOnlyList<ScopePair> ScopePairs);
