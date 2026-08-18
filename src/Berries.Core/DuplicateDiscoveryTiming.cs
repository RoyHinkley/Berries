namespace Berries.Core;

/// <summary>Elapsed time spent in the major duplicate-discovery phases.</summary>
public sealed record DuplicateDiscoveryTiming(
    TimeSpan SizeGrouping,
    TimeSpan ContentHashing,
    TimeSpan DuplicateSetConstruction,
    TimeSpan Total);
