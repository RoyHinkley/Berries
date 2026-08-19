namespace Berries.Core;

public sealed record ScopeAnalysisTiming(
    TimeSpan EvidenceConstruction,
    TimeSpan ScopeAggregation,
    TimeSpan ResultConstruction,
    TimeSpan Total);
