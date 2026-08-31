using Berries.Core.Domain;

namespace Berries.Core.Analysis;

/// <summary>
/// Content identity discovered as duplicated during the primary scan, together with its
/// current Working-Portrait members. Membership may fall to one or zero during a session.
/// </summary>
public sealed record Group(ContentId Content, IReadOnlyList<FileInstance> Files)
{
    public int FileCount => Files.Count;
}
