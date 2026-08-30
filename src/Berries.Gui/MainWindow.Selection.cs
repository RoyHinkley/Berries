using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Berries.Core.Domain;

namespace Berries.Gui;

public partial class MainWindow
{
    private ExplorerNode? focusedNode;
    private bool synchronizingSelection;

    private void ExplorerNode_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ExplorerNode node } || controller.Session is not { } session) return;
        focusedNode = node;
        session.Selection.Toggle(node.Files);
        SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
        e.Handled = true;
    }

    private void ClearSelectionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not { } session) return;
        session.Selection.Clear(); SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
    }

    private void InvertSelectedCopies_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not { } session || session.Selection.IsEmpty) return;
        session.Selection.InvertSelectedCopies(session.DuplicateSets);
        SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
    }

    private void InvertAllGroups_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not { } session || !IsGroupsProjection()) return;
        session.Selection.Invert(RepresentedFiles());
        SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
    }

    private bool IsGroupsProjection() => !PairExplorer.IsVisible && currentScope is null
        && ProjectionTitle.Text?.StartsWith("Group", StringComparison.Ordinal) == true;

    private IReadOnlyList<FileInstance> RepresentedFiles() => DistinctFilesFast(
        ActiveTrees().SelectMany(tree => EnumerateNodes(tree.ItemsSource))
            .Where(node => node.Children.Count == 0 && node.Files.Count == 1).SelectMany(node => node.Files));

    private IEnumerable<TreeView> ActiveTrees()
    {
        if (PairExplorer.IsVisible) { yield return LeftTree; yield return RightTree; }
        else yield return ExplorerTree;
    }

    private void SynchronizeVisibleSelection()
    {
        if (controller.Session is not { } session) return;
        synchronizingSelection = true;
        try
        {
            foreach (var tree in ActiveTrees())
            {
                if (tree.SelectedItems is null) continue;
                tree.SelectedItems.Clear();
                foreach (var leaf in EnumerateNodes(tree.ItemsSource)
                    .Where(node => node.Children.Count == 0 && node.Files.Count == 1 && session.Selection.Contains(node.Files[0])))
                    tree.SelectedItems.Add(leaf);
            }
        }
        finally { synchronizingSelection = false; }
    }

    private void UpdateSelectionSummary()
    {
        var session = controller.Session;
        if (session is null || session.Selection.IsEmpty)
        {
            SelectionText.Text = "Selected: none"; SetSelectionCapabilities(false); return;
        }

        var selection = session.Selection;
        var groups = selection.CountGroups(session.DuplicateSets);
        var outside = selection.CountOutside(RepresentedFiles());
        SelectionText.Text = $"Selected: {selection.Count:N0} files · {groups:N0} groups · {outside:N0} outside view";
        SetSelectionCapabilities(true);
    }

    private void SetSelectionCapabilities(bool hasSelection)
    {
        ClearSelectionButton.IsEnabled = hasSelection;
        InvertButton.IsEnabled = hasSelection;
        InvertSelectedCopiesMenu.IsEnabled = hasSelection;
        InvertAllGroupsMenu.IsEnabled = hasSelection && IsGroupsProjection();
        ExcludeButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private IReadOnlyList<FileInstance> SemanticSelection() => controller.Session?.Selection.Files ?? [];

    private IReadOnlyList<FileInstance> SemanticSelectionInScope(Berries.FileSystem.Abstractions.FileSystemPath scope, bool descendants) =>
        SemanticSelection().Where(file => fileSystem.PathsEqual(file.ParentDirectory, scope)
            || (descendants && fileSystem.IsDescendant(file.ParentDirectory, scope))).ToArray();
}
