using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Berries.Gui;

/// <summary>
/// Virtualized flat presentation of an ExplorerNode hierarchy.
///
/// Avalonia TreeView virtualizes top-level items, so an expanded root becomes one
/// very tall virtualized item. That makes the virtualizer's size estimates unstable
/// when branches expand. This control instead virtualizes the visible rows directly.
/// </summary>
public sealed class BerriesTreeView : ListBox
{
    private readonly ObservableCollection<ExplorerRow> rows = [];
    private IEnumerable? hierarchyItemsSource;
    private INotifyCollectionChanged? hierarchyNotifier;

    public BerriesTreeView()
    {
        base.ItemsSource = rows;
        SelectionMode = SelectionMode.Multiple;
    }

    protected override Type StyleKeyOverride => typeof(ListBox);

    public new IEnumerable? ItemsSource
    {
        get => hierarchyItemsSource;
        set
        {
            if (ReferenceEquals(hierarchyItemsSource, value))
                return;

            if (hierarchyNotifier is not null)
                hierarchyNotifier.CollectionChanged -= HierarchyCollectionChanged;

            hierarchyItemsSource = value;
            hierarchyNotifier = value as INotifyCollectionChanged;

            if (hierarchyNotifier is not null)
                hierarchyNotifier.CollectionChanged += HierarchyCollectionChanged;

            RebuildRows();
        }
    }

    public IReadOnlyList<ExplorerRow> Rows => rows;

    public void ToggleExpansion(ExplorerNode node)
    {
        if (node.Children.Count == 0)
            return;

        node.IsExpanded = !node.IsExpanded;
        RebuildRows();
    }

    public void RefreshRows() => RebuildRows();

    private void HierarchyCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildRows();

    private void RebuildRows()
    {
        rows.Clear();

        if (hierarchyItemsSource is null)
            return;

        foreach (var node in hierarchyItemsSource.OfType<ExplorerNode>())
            AppendVisible(node, 0);
    }

    private void AppendVisible(ExplorerNode node, int depth)
    {
        rows.Add(new ExplorerRow(node, depth));

        if (!node.IsExpanded)
            return;

        foreach (var child in node.Children)
            AppendVisible(child, depth + 1);
    }
}

public sealed record ExplorerRow(ExplorerNode Node, int Depth)
{
    public string Label => Node.Label;
    public bool HasChildren => Node.Children.Count > 0;
    public Thickness Indent => new(Depth * 18, 0, 0, 0);
}
