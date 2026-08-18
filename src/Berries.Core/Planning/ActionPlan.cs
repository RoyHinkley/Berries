namespace Berries.Core.Planning;

/// <summary>Deterministic logical transformation implementing one Disposition.</summary>
public sealed record ActionPlan(IReadOnlyList<FileAction> Actions);
