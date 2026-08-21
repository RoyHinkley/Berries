using Berries.Core.Analysis;

namespace Berries.Core;

public sealed record BranchAnalysisResult(
    IReadOnlyList<BranchPair> BranchPairs,
    BranchAnalysisTiming Timing);
