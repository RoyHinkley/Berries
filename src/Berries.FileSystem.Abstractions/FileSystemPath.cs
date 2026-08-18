namespace Berries.FileSystem.Abstractions;

/// <summary>
/// An adapter-defined canonical filesystem path. Core treats the value as opaque;
/// path syntax and identity rules belong to the filesystem adapter.
/// </summary>
public readonly record struct FileSystemPath(string Value)
{
    public override string ToString() => Value;
}
