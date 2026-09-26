using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Berries.Gui;

/// <summary>
/// TreeView whose selection state is controlled by Berries rather than by native
/// TreeView pointer or keyboard selection. SelectedItems is used only to portray
/// the persistent semantic file selection.
///
/// Avalonia's virtualized TreeView can change its ScrollViewer offset when a node
/// expands or collapses. Preserve the operated node's viewport position across
/// that layout change instead: expansion should reveal children in place, not
/// move the node the user just operated.
/// </summary>
public sealed class BerriesTreeView : TreeView
{
    private TreeViewItem? expansionAnchor;
    private double expansionAnchorY;
    private bool awaitingExpansionLayout;

    public BerriesTreeView()
    {
        AddHandler(TreeViewItem.ExpandedEvent, ExpansionChanged);
        AddHandler(TreeViewItem.CollapsedEvent, ExpansionChanged);
    }

    protected override Type StyleKeyOverride => typeof(TreeView);

    protected override bool ShouldTriggerSelection(Visual selectable, PointerEventArgs eventArgs) => false;

    protected override bool ShouldTriggerSelection(Visual selectable, KeyEventArgs eventArgs) => false;

    private void ExpansionChanged(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TreeViewItem item
            || item.TranslatePoint(default, this) is not { } position)
            return;

        expansionAnchor = item;
        expansionAnchorY = position.Y;

        if (awaitingExpansionLayout) return;
        awaitingExpansionLayout = true;
        LayoutUpdated += RestoreExpansionAnchor;
    }

    private void RestoreExpansionAnchor(object? sender, EventArgs e)
    {
        LayoutUpdated -= RestoreExpansionAnchor;
        awaitingExpansionLayout = false;

        var item = expansionAnchor;
        expansionAnchor = null;
        if (item?.TranslatePoint(default, this) is not { } position)
            return;

        var delta = position.Y - expansionAnchorY;
        if (Math.Abs(delta) < 0.5)
            return;

        var scrollViewer = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
            return;

        var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var newY = Math.Clamp(scrollViewer.Offset.Y + delta, 0, maxY);
        scrollViewer.Offset = scrollViewer.Offset.WithY(newY);
    }
}
