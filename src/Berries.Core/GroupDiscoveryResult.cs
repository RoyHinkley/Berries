using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core;

public sealed record GroupDiscoveryResult(
    Portrait Portrait,
    IReadOnlyList<Group> Groups,
    IReadOnlyDictionary<FileSystemPath, int> UniqueFileCountsByDirectory,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<FileEviction> Evictions,
    GroupDiscoveryTiming Timing)
{
    public int GroupedFileCount => Groups.Sum(group => group.FileCount);
}
