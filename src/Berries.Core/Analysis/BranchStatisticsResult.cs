namespace Berries.Core.Analysis;

public sealed record BranchStatisticsResult(
    IReadOnlyList<BranchRecord> Branches,
    TimeSpan Elapsed);
