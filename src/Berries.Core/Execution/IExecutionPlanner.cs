using Berries.Core.Domain;
using Berries.Core.Planning;

namespace Berries.Core.Execution;

public interface IExecutionPlanner
{
    ExecutionPlan Build(Portrait initialPortrait, Portrait finalPortrait, IReadOnlyList<ActionPlan> actionPlans);
}
