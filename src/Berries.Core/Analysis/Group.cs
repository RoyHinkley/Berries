using Berries.Core.Domain;

namespace Berries.Core.Analysis;

/// <summary>All currently duplicated files having one identical content identity.</summary>
public sealed record Group(ContentId Content, IReadOnlyList<FileInstance> Files)
{
    public int FileCount => Files.Count;
}
