using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Projection;

public sealed record BranchProjection(
    FileSystemPath Branch,
    BranchProjectionNode Root);

public sealed class BranchProjectionNode(
    string label,
    FileSystemPath? directory = null,
    FileInstance? file = null)
{
    public string Label { get; } = label;
    public FileSystemPath? Directory { get; } = directory;
    public FileInstance? File { get; } = file;
    public List<BranchProjectionNode> Children { get; } = [];
    public IReadOnlyList<FileInstance> Files { get; internal set; } = file is null ? [] : [file];
}
