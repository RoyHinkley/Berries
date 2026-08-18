namespace Berries.Core.Domain;

/// <summary>A modeled filesystem state; immutable snapshots are preferred as implementation develops.</summary>
public sealed class Portrait
{
    public Portrait(IEnumerable<FileInstance> files) => Files = files.ToArray();
    public IReadOnlyList<FileInstance> Files { get; }
}
