using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Interactivity;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

public partial class MainWindow
{
    private FileSystemPath? focusedScope;
    private string? readyStatus;

    private void CancelRootsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null)
            return;

        RootsPanel.IsVisible = false;
        ExplorerPanel.IsVisible = true;
        StatusText.Text = readyStatus ?? "Returned to the current session.";
    }

    private void ExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = sender is TreeView tree && tree.SelectedItems is { Count: > 0 };
        InvertButton.IsEnabled = hasSelection;
        ExcludeButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        UpdatePivotCapabilities();
        UpdateSelectionStatus();
    }

    private void UpdatePivotCapabilities()
    {
        var selectedNodes = SelectedNodesFromActiveProjection();
        var selectedFiles = DistinctFiles(selectedNodes.SelectMany(node => node.Files));
        var oneNode = selectedNodes.Count == 1 ? selectedNodes[0] : null;
        var oneFile = selectedFiles.Count == 1 ? selectedFiles[0] : null;

        PivotContentMenu.IsEnabled = selectedFiles.Any(file => file.Content is not null);
        PivotDirectoryMenu.IsEnabled = oneFile is not null || oneNode?.Scope is not null;
        PivotBranchMenu.IsEnabled = oneFile is not null || oneNode?.Scope is not null;

        var suggestions = controller.Counterparts?.Seeds;
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private void UpdateSelectionStatus()
    {
        if (StatusProgress.IsVisible)
            return;

        var nodes = SelectedNodesFromActiveProjection();
        if (nodes.Count == 0)
        {
            if (readyStatus is not null)
                StatusText.Text = readyStatus;
            return;
        }

        var files = DistinctFiles(nodes.SelectMany(node => node.Files));
        var contents = files.Where(file => file.Content is not null)
            .Select(file => file.Content!.Value)
            .Distinct()
            .Count();

        if (files.Count == 1)
        {
            var file = files[0];
            StatusText.Text = $"{file.Path.Value} — {file.Length:N0} bytes";
        }
        else if (files.Count > 0)
        {
            StatusText.Text = $"Selection — {files.Count:N0} duplicate instances, {contents:N0} Contents";
        }
        else
        {
            StatusText.Text = nodes.Count == 1 ? nodes[0].Label : $"Selection — {nodes.Count:N0} items";
        }
    }

    private IReadOnlyList<ExplorerNode> SelectedNodesFromActiveProjection()
    {
        IEnumerable<object> selected = PairExplorer.IsVisible
            ? SelectedObjects(LeftTree).Concat(SelectedObjects(RightTree))
            : SelectedObjects(ExplorerTree);

        return selected.OfType<TreeViewItem>()
            .Select(item => item.Tag)
            .OfType<ExplorerNode>()
            .Distinct()
            .ToArray();
    }

    private static IEnumerable<object> SelectedObjects(TreeView tree) =>
        tree.SelectedItems?.Cast<object>() ?? [];

    private void PivotDirectory_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedDirectoryScope();
        if (scope is null) return;
        ShowScopeProjection(scope.Value, includeDescendants: false, "Directory");
    }

    private void PivotBranch_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedDirectoryScope();
        if (scope is null) return;
        ShowScopeProjection(scope.Value, includeDescendants: true, "Branch");
    }

    private FileSystemPath? SelectedDirectoryScope()
    {
        var nodes = SelectedNodesFromActiveProjection();
        if (nodes.Count != 1) return null;
        if (nodes[0].Scope is not null) return nodes[0].Scope;

        var files = DistinctFiles(nodes[0].Files);
        return files.Count == 1 ? files[0].ParentDirectory : null;
    }

    private void ShowScopeProjection(FileSystemPath scope, bool includeDescendants, string title)
    {
        var session = controller.Session;
        if (session is null) return;

        focusedScope = scope;
        leftScope = null;
        rightScope = null;
        PairExplorer.IsVisible = false;
        SingleExplorer.IsVisible = true;
        ProjectionTitle.Text = $"{title} — {scope.Value}";

        if (includeDescendants)
        {
            ExplorerTree.ItemsSource = new[] { CreateTreeItem(BuildBranchTree(scope)) };
        }
        else
        {
            var files = session.DuplicateSets.SelectMany(set => set.Files)
                .Where(file => fileSystem.PathsEqual(file.ParentDirectory, scope))
                .OrderBy(file => file.Path.Value, StringComparer.OrdinalIgnoreCase)
                .Select(file => new ExplorerNode(Path.GetFileName(file.Path.Value), [file], file.ParentDirectory))
                .ToArray();
            var root = new ExplorerNode(scope.Value, files.SelectMany(node => node.Files), scope);
            foreach (var file in files) root.Children.Add(file);
            ExplorerTree.ItemsSource = new[] { CreateTreeItem(root) };
        }

        UpdateCapabilities();
        UpdatePivotCapabilities();
    }

    private void UpdateRootsCancelCapability() =>
        CancelRootsButton.IsEnabled = controller.Session is not null;
}
