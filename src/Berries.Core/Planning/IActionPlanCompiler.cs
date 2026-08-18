using Berries.Core.Decisions;
using Berries.Core.Domain;

namespace Berries.Core.Planning;

public interface IActionPlanCompiler
{
    ActionPlan Compile(Portrait portrait, Disposition disposition);
}
