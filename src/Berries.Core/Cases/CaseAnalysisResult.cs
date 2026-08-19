namespace Berries.Core.Cases;

public sealed record CaseAnalysisResult(
    IReadOnlyList<Case> TopCases,
    int TotalCaseCount,
    int DuplicateSetCaseCount,
    int SingleDirectoryCaseCount,
    int DirectoryPairCaseCount,
    int ScopePairCaseCount);
