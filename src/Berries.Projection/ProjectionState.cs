using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Projection;

public enum ProjectionKind
{
    Corpus,
    CorpusRoots,
    Groups,
    DirectoryNamesakes,
    Directory,
    Branch,
    DirectoryPair,
    BranchPair
}

/// <summary>
/// Presentation state for the current Explorer projection.
/// This is navigation/view state, not a domain Case and not disposition authority.
/// </summary>
public sealed record ProjectionState(
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
