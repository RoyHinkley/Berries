using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

internal sealed class ExplorerNode
{
    public ExplorerNode(
        string label,
        IEnumerable<FileInstance>? files = null,
        FileSystemPath? scope = null)
    {
        Label = label;
        Files = files?.ToArray() ?? [];
        Scope = scope;
    }

    public string Label { get; }
    public IReadOnlyList<FileInstance> Files { get; set; }
    public FileSystemPath? Scope { get; }
    public List<ExplorerNode> Children { get; } = [];
}
