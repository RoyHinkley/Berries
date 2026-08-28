using Berries.Core.Domain;

namespace Berries.Gui;

internal sealed class ExplorerNode
{
    public ExplorerNode(string label, IEnumerable<FileInstance>? files = null)
    {
        Label = label;
        Files = files?.ToArray() ?? [];
    }

    public string Label { get; }
    public IReadOnlyList<FileInstance> Files { get; set; }
    public List<ExplorerNode> Children { get; } = [];
}
