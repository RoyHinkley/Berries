using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record ScopePair(
    FileSystemPath FirstRoot,
    FileSystemPath SecondRoot,
    int ApproximateLeverage);
