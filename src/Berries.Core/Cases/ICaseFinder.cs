using Berries.Core.Analysis;
using Berries.Core.Domain;

namespace Berries.Core.Cases;

public interface ICaseFinder
{
    IReadOnlyList<Case> FindCases(Portrait portrait, PortraitAnalysis analysis);
}
