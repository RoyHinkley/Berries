using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Projection;

public enum ProjectionKind
{
    Corpus,
    CorpusRoots,
    Groups,
    Directory,
    Branch,
    DirectoryPair,
    BranchPair
}

/// <summary>
/// The bounded data set underlying one presented Explorer projection.
/// A Projection is a presentation of a Case; changing projection can change
/// how the same or a related Case is organized without changing the Working Portrait.
/// </summary>
public sealed record Case(
    ProjectionKind Kind,
    IReadOnlyList<FileInstance> RepresentedFiles,
    FileSystemPath? Primary = null,
    FileSystemPath? Secondary = null,
    IReadOnlyList<FileInstance>? PrimaryFiles = null,
    IReadOnlyList<FileInstance>? SecondaryFiles = null)
{
    public bool IsPair => Kind is ProjectionKind.DirectoryPair or ProjectionKind.BranchPair;
    public bool IncludesDescendants => Kind is ProjectionKind.Branch or ProjectionKind.BranchPair or ProjectionKind.CorpusRoots;
}
