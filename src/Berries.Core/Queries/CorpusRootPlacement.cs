using Berries.FileSystem.Abstractions;

namespace Berries.Core.Queries;

public sealed record CorpusRootPlacement(
    FileSystemPath Root,
    IReadOnlyList<BranchFilePlacement> Files);
