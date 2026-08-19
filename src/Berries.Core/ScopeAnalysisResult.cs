using Berries.Core.Analysis;

namespace Berries.Core;

public sealed record ScopeAnalysisResult(
    IReadOnlyList<ScopePair> ScopePairs,
    ScopeAnalysisTiming Timing);
