using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

internal enum ProjectionKind
{
    Corpus,
    CorpusRoots,
    Groups,
    Directory,
    Branch,
    DirectoryPair,
    BranchPair
}

internal sealed record ProjectionState(
    ProjectionKind Kind,
    IReadOnlyList<FileInstance> RepresentedFiles,
    FileSystemPath? Primary = null,
    FileSystemPath? Secondary = null)
{
    public bool IsPair => Kind is ProjectionKind.DirectoryPair or ProjectionKind.BranchPair;
    public bool IncludesDescendants => Kind is ProjectionKind.Branch or ProjectionKind.BranchPair or ProjectionKind.CorpusRoots;
}
