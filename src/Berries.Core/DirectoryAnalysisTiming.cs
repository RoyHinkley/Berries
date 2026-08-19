namespace Berries.Core;

public sealed record DirectoryAnalysisTiming(
    TimeSpan DirectoryRecords,
    TimeSpan DirectoryPairs,
    TimeSpan Total);
