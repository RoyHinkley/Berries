using Berries.Core.Domain;

namespace Berries.Core.Analysis;

public sealed record DuplicateSet(ContentId Content, IReadOnlyList<FileInstance> Files)
{
    public int InstanceCount => Files.Count;
}
