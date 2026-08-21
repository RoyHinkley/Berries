namespace Berries.Core.Cases;

public sealed record CaseAnalysisTiming(
    TimeSpan CandidateConstruction,
    TimeSpan Ranking,
    TimeSpan Materialization,
    TimeSpan Total);

public sealed record CaseAnalysisResult(
    IReadOnlyList<Case> TopCases,
    int TotalCaseCount,
    int DuplicateSetCaseCount,
    int SingleDirectoryCaseCount,
    int DirectoryPairCaseCount,
    int BranchPairCaseCount,
    CaseAnalysisTiming Timing)
{
    public TimeSpan TotalElapsed => Timing.Total;
}
