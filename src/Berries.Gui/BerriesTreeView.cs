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

        var rowIndex = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i].Node, node))
            {
                rowIndex = i;
                break;
            }
        }

        if (rowIndex < 0)
            return;

        var depth = rows[rowIndex].Depth;
        if (node.IsExpanded)
        {
            node.IsExpanded = false;

            // Visible descendants are contiguous in the flat list. Remove only them;
            // preserving all rows above the branch keeps the virtualizer's viewport
            // mapping stable.
            while (rowIndex + 1 < rows.Count && rows[rowIndex + 1].Depth > depth)
                rows.RemoveAt(rowIndex + 1);
            return;
        }

        node.IsExpanded = true;
        var insertAt = rowIndex + 1;
        foreach (var child in node.Children)
        {
            var added = FlattenVisible(child, depth + 1);
            foreach (var row in added)
                rows.Insert(insertAt++, row);
        }
    }

    public void RefreshRows() => RebuildRows();

    private void HierarchyCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Groups are published incrementally into an ObservableCollection. Appending a
        // collapsed top-level node is also exactly one visible-row append; rebuilding
        // all preceding rows here would make incremental publication O(N²).
        if (e.Action == NotifyCollectionChangedAction.Add
            && e.NewItems is not null
            && e.NewStartingIndex >= 0)
        {
            var insertAt = VisibleRowIndexForRoot(e.NewStartingIndex);
            foreach (var node in e.NewItems.OfType<ExplorerNode>())
            {
                var added = FlattenVisible(node, 0);
                foreach (var row in added)
                    rows.Insert(insertAt++, row);
            }
            return;
        }

        RebuildRows();
    }

    private int VisibleRowIndexForRoot(int rootIndex)
    {
        if (hierarchyItemsSource is not IList roots)
            return rows.Count;

        var visible = 0;
        for (var i = 0; i < rootIndex && i < roots.Count; i++)
            if (roots[i] is ExplorerNode node)
                visible += CountVisible(node);

        return visible;
    }

    private static int CountVisible(ExplorerNode node)
    {
        var count = 1;
        if (node.IsExpanded)
            foreach (var child in node.Children)
                count += CountVisible(child);
        return count;
    }

    private static List<ExplorerRow> FlattenVisible(ExplorerNode node, int depth)
    {
        var result = new List<ExplorerRow>();
        AppendVisible(node, depth, result);
        return result;
    }

    private static void AppendVisible(ExplorerNode node, int depth, List<ExplorerRow> result)
    {
        result.Add(new ExplorerRow(node, depth));
        if (node.IsExpanded)
            foreach (var child in node.Children)
                AppendVisible(child, depth + 1, result);
    }

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
