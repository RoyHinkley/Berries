using Berries.Core.Analysis;

namespace Berries.Core;

public sealed record DirectoryAnalysisResult(
    IReadOnlyList<DirectoryRecord> Directories,
    IReadOnlyList<DirectoryPair> DirectoryPairs,
    DirectoryGraphAnalysis Graph,
    DirectoryAnalysisTiming Timing);
