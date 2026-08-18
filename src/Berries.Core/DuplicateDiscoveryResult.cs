using Berries.Core.Analysis;

namespace Berries.Core;

public sealed record DuplicateDiscoveryResult(
    IReadOnlyList<DuplicateSet> DuplicateSets,
    DuplicateDiscoveryTiming Timing)
{
    public int DuplicateFileCount => DuplicateSets.Sum(set => set.InstanceCount);
}
