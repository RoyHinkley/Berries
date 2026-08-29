namespace Berries.Core;

/// <summary>
/// Progress reported by Core operations. The engine owns the meaning and units of work;
/// consumers only present them. A null Total denotes work whose extent is not knowable
/// in advance and should normally be presented as indeterminate progress.
/// </summary>
public sealed record OperationProgress(
    string Phase,
    long? Completed = null,
    long? Total = null);
