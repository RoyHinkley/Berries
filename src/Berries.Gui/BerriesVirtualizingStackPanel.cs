using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Berries.Gui;

/// <summary>
/// Virtualizing panel for Explorer trees.
///
/// Avalonia's VirtualizingStackPanel registers realized rows as scroll anchors.
/// Expanding or collapsing a TreeViewItem changes the positions of those rows,
/// causing ScrollContentPresenter to compensate by moving the viewport by roughly
/// the size of the inserted or removed subtree. Explorer expansion is itself the
/// intentional layout change, so that compensation is undesirable here.
///
/// Keep virtualization, but prevent Explorer rows from participating in scroll
/// anchoring. The panel unregisters existing candidates before layout (clearing
/// any previously chosen anchor) and candidates registered by the base arrange
/// afterward.
/// </summary>
public sealed class BerriesVirtualizingStackPanel : VirtualizingStackPanel
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        var scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        UnregisterChildren(scrollViewer);

        var result = base.ArrangeOverride(finalSize);

        UnregisterChildren(scrollViewer);
        return result;
    }

    private void UnregisterChildren(ScrollViewer? scrollViewer)
    {
        if (scrollViewer is null) return;

        foreach (var child in Children)
            scrollViewer.UnregisterAnchorCandidate(child);
    }
}
