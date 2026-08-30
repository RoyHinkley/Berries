namespace Berries.Core;

/// <summary>Elapsed time spent in the major Group-discovery phases.</summary>
public sealed record GroupDiscoveryTiming(
    TimeSpan SizeGrouping,
    TimeSpan ContentHashing,
    TimeSpan GroupConstruction,
    TimeSpan Total);
