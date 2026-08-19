namespace Berries.Core.Cases;

public sealed record CaseAnalysisResult(IReadOnlyList<Case> Cases)
{
    public int DuplicateSetCaseCount => Cases.Count(item => item is DuplicateSetCase);
    public int SingleDirectoryCaseCount => Cases.Count(item => item is SingleDirectoryCase);
    public int DirectoryPairCaseCount => Cases.Count(item => item is DirectoryPairCase);
    public int ScopePairCaseCount => Cases.Count(item => item is ScopePairCase);
}
