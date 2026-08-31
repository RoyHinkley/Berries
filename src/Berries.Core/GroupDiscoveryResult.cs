using Berries.Core.Analysis;
using Berries.Core.Domain;

namespace Berries.Core;

public sealed record GroupDiscoveryResult(
    Portrait Portrait,
    IReadOnlyList<Group> Groups,
    IReadOnlyList<FileEviction> Evictions,
    GroupDiscoveryTiming Timing)
{
    public int GroupedFileCount => Groups.Sum(group => group.FileCount);
}
