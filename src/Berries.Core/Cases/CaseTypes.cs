using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Cases;

public sealed record DuplicateSetCase(DuplicateSet DuplicateSet)
    : Case(DuplicateSet.Files, 1);

public sealed record SingleDirectoryCase(
    FileSystemPath Directory,
    IReadOnlyList<FileInstance> BoundedFiles,
    int DuplicateContentCount)
    : Case(BoundedFiles, DuplicateContentCount);

public sealed record DirectoryPairCase(
    DirectoryPair Pair,
    IReadOnlyList<FileInstance> BoundedFiles)
    : Case(BoundedFiles, Pair.SharedContentCount);

public sealed record ScopePairCase(
    ScopePair Pair,
    IReadOnlyList<FileInstance> BoundedFiles)
    : Case(BoundedFiles, Pair.ApproximateLeverage);
