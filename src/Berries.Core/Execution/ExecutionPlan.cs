namespace Berries.Core.Execution;

/// <summary>Safe physical realization of accumulated logical ActionPlans.</summary>
public sealed record ExecutionPlan(IReadOnlyList<ExecutionStep> Steps);

public abstract record ExecutionStep;
