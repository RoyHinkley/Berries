namespace Berries.Core;

public sealed record BranchAnalysisTiming(
    TimeSpan EvidenceConstruction,
    TimeSpan BranchAggregation,
    TimeSpan ResultConstruction,
    TimeSpan Total);
