using Berries.Core.Analysis;

namespace Berries.Core;

public sealed record BranchAnalysisResult(
    IReadOnlyList<BranchRecord> Branches,
    IReadOnlyList<BranchPair> BranchPairs,
    BranchAnalysisTiming Timing);
