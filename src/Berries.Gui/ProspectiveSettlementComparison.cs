using Berries.Core.Analysis;
using Berries.Core.Cases;
using Berries.Core.Domain;

namespace Berries.Gui;

public sealed record ProspectiveSettlementCandidate(
    DuplicateSet DuplicateSet,
    string CommonFileName,
    int InstanceCount,
    int DirectoryCount,
    long InducedDirectoryPairCount,
    double MeanOtherSharedContent,
    int MaxOtherSharedContent);

public sealed record ProspectiveSettlementComparison(
    ProspectiveSettlementCandidate Candidate,
    DirectoryAnalysisResult BaselineDirectoryAnalysis,
    ScopeAnalysisResult BaselineScopeAnalysis,
    CaseAnalysisResult BaselineCaseAnalysis,
    DirectoryAnalysisResult SettledDirectoryAnalysis,
    ScopeAnalysisResult SettledScopeAnalysis,
    CaseAnalysisResult SettledCaseAnalysis,
    int TopCaseOverlapCount,
    int SameRankTopCaseCount,
    TimeSpan TotalElapsed);
