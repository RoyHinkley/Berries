using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Berries.Gui;

/// <summary>
/// TreeView whose semantic selection is controlled by Berries rather than by
/// Avalonia's native TreeView selection model.
/// </summary>
public sealed class BerriesTreeView : TreeView
{
    private ScrollViewer? diagnosticScrollViewer;
    private double diagnosticOffsetBefore;
    private string? diagnosticNode;
    private bool diagnosticLayoutPending;

    public BerriesTreeView()
    {
        AddHandler(TreeViewItem.ExpandedEvent, ExpansionChanged, RoutingStrategies.Bubble);
        AddHandler(TreeViewItem.CollapsedEvent, ExpansionChanged, RoutingStrategies.Bubble);
        LayoutUpdated += DiagnosticLayoutUpdated;
    }

    protected override Type StyleKeyOverride => typeof(TreeView);

    protected override bool ShouldTriggerSelection(Visual selectable, PointerEventArgs eventArgs) => false;

    protected override bool ShouldTriggerSelection(Visual selectable, KeyEventArgs eventArgs) => false;

    private void ExpansionChanged(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TreeViewItem item)
            return;

        diagnosticScrollViewer ??= this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (diagnosticScrollViewer is null)
            return;

        diagnosticOffsetBefore = diagnosticScrollViewer.Offset.Y;
        diagnosticNode = (item.DataContext as ExplorerNode)?.Label ?? "<unknown>";
        diagnosticLayoutPending = true;

        Debug.WriteLine(
            $"BERRIES TREE EXPANSION before: node={diagnosticNode}, offset={diagnosticOffsetBefore:F2}");
    }

    private void DiagnosticLayoutUpdated(object? sender, EventArgs e)
    {
        if (!diagnosticLayoutPending || diagnosticScrollViewer is null)
            return;

        diagnosticLayoutPending = false;
        Debug.WriteLine(
            $"BERRIES TREE EXPANSION after:  node={diagnosticNode}, offset={diagnosticScrollViewer.Offset.Y:F2}");
    }
}
