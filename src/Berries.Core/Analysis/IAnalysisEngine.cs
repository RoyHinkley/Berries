using Berries.Core.Domain;

namespace Berries.Core.Analysis;

public interface IAnalysisEngine
{
    PortraitAnalysis Analyze(Portrait portrait);
}
