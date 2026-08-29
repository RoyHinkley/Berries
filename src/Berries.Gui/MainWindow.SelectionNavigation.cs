using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Berries.Gui;

public partial class MainWindow
{
    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !ExplorerPanel.IsVisible)
            return;

        if (PairExplorer.IsVisible)
        {
            ClearTreeSelection(LeftTree);
            ClearTreeSelection(RightTree);
        }
        else
        {
            ClearTreeSelection(ExplorerTree);
        }

        InvertButton.IsEnabled = false;
        ExcludeButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        e.Handled = true;
    }

    private static void ClearTreeSelection(TreeView tree) =>
        tree.SelectedItems?.Clear();

    private void PivotButton_Click(object? sender, RoutedEventArgs e)
    {
        // Keep opening the Pivot menu cheap. Resolve descendant files and counterpart
        // analyses only after the user chooses an operation.
        var nodes = SelectedNodesFromActiveProjection();
        var hasSession = controller.Session is not null;
        var canResolveScope = nodes.Count == 1 || (nodes.Count == 0 && currentScope is not null);

        PivotCorpusRootsMenu.IsEnabled = hasSession;
        PivotContentMenu.IsEnabled = hasSession;
        PivotDirectoryMenu.IsEnabled = hasSession && canResolveScope;
        PivotBranchMenu.IsEnabled = hasSession && canResolveScope;
        PivotBestDirectoryPairMenu.IsEnabled = hasSession && canResolveScope;
        PivotBestBranchPairMenu.IsEnabled = hasSession && canResolveScope;

        var suggestions = controller.Counterparts?.Seeds;
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private void ExplorerNode_ContextRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ExplorerNode node } control)
            return;

        var tree = TreeContaining(node);
        if (tree?.SelectedItems is not null && node.Files.Count > 0)
        {
            tree.SelectedItems.Clear();
            tree.SelectedItems.Add(node);
        }

        PivotButton_Click(PivotButton, e);
        PivotButton.Flyout?.ShowAt(control);
        e.Handled = true;
    }

    private TreeView? TreeContaining(ExplorerNode node)
    {
        if (SingleExplorer.IsVisible && EnumerateNodes(ExplorerTree.ItemsSource).Any(candidate => ReferenceEquals(candidate, node)))
            return ExplorerTree;
        if (PairExplorer.IsVisible && EnumerateNodes(LeftTree.ItemsSource).Any(candidate => ReferenceEquals(candidate, node)))
            return LeftTree;
        if (PairExplorer.IsVisible && EnumerateNodes(RightTree.ItemsSource).Any(candidate => ReferenceEquals(candidate, node)))
            return RightTree;
        return null;
    }

    private void PivotContentOrAll_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null)
            return;

        if (SelectedNodesFromActiveProjection().Count > 0)
        {
            PivotSelectedContent_Click(sender, e);
            return;
        }

        currentScope = null;
        BreadcrumbPanel.IsVisible = false;
        BreadcrumbPanel.Children.Clear();
        ShowContentProjection();
    }
}
