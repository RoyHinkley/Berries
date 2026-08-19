using Berries.Core.Analysis;
using Berries.Core.Domain;

namespace Berries.Core;

public sealed record DuplicateDiscoveryResult(
    Portrait Portrait,
    IReadOnlyList<DuplicateSet> DuplicateSets,
    IReadOnlyList<FileEviction> Evictions,
    DuplicateDiscoveryTiming Timing)
{
    public int DuplicateFileCount => DuplicateSets.Sum(set => set.InstanceCount);
}
