using Berries.Core.Domain;

namespace Berries.Core.Analysis;

/// <summary>Entry point for duplicate, directory, pair, scope, and case analysis.</summary>
public sealed class AnalysisEngine : IAnalysisEngine
{
    public PortraitAnalysis Analyze(Portrait portrait) =>
        throw new NotImplementedException("Duplicate and structural analysis has not been implemented yet.");
}
