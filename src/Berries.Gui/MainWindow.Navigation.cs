using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Interactivity;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

public partial class MainWindow
{
    private void CancelRootsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null)
            return;

        RootsPanel.IsVisible = false;
        ExplorerPanel.IsVisible = true;
        StatusText.Text = "Returned to the current session.";
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
        var nodes = SelectedNodesFromActiveProjection();
        var files = DistinctFiles(nodes.SelectMany(node => node.Files));
        var oneNode = nodes.Count == 1 ? nodes[0] : null;

        PivotContentMenu.IsEnabled = files.Any(file => file.Content is not null);
        PivotDirectoryMenu.IsEnabled = oneNode is not null && InferSelectedScope(oneNode) is not null;
        PivotBranchMenu.IsEnabled = PivotDirectoryMenu.IsEnabled;

        var suggestions = controller.Counterparts?.Seeds;
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private void UpdateSelectionStatus()
    {
        if (StatusProgress.IsVisible)
            return;

        var nodes = SelectedNodesFromActiveProjection();
        if (nodes.Count == 0)
            return;

        var files = DistinctFiles(nodes.SelectMany(node => node.Files));
        var contents = files.Where(file => file.Content is not null)
            .Select(file => file.Content!.Value)
            .Distinct()
            .Count();

        if (files.Count == 1)
            StatusText.Text = $"{files[0].Path.Value} — {files[0].Length:N0} bytes";
        else if (files.Count > 0)
            StatusText.Text = $"Selection — {files.Count:N0} duplicate instances, {contents:N0} Contents";
        else
            StatusText.Text = nodes.Count == 1 ? nodes[0].Label : $"Selection — {nodes.Count:N0} items";
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

    private void PivotSelectedContent_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session;
        if (session is null) return;

        var selected = DistinctFiles(SelectedNodesFromActiveProjection().SelectMany(node => node.Files));
        var contentIds = selected.Where(file => file.Content is not null)
            .Select(file => file.Content!.Value)
            .Distinct()
            .ToHashSet();
        if (contentIds.Count == 0) return;

        leftScope = null;
        rightScope = null;
        PairExplorer.IsVisible = false;
        SingleExplorer.IsVisible = true;
        ProjectionTitle.Text = contentIds.Count == 1 ? "Content" : $"Content — {contentIds.Count:N0} selected Contents";

        var nodes = session.DuplicateSets
            .Where(set => contentIds.Contains(set.Content))
            .OrderByDescending(set => set.Files.Count)
            .Select(set =>
            {
                var names = set.Files.Select(file => Path.GetFileName(file.Path.Value))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var title = names.Length == 1
                    ? $"{names[0]}  —  {set.Files.Count:N0} instances"
                    : $"Content {ShortContent(set.Content)}  —  {set.Files.Count:N0} instances";
                var node = new ExplorerNode(title, set.Files);
                foreach (var file in set.Files.OrderBy(file => file.Path.Value, StringComparer.OrdinalIgnoreCase))
                    node.Children.Add(new ExplorerNode(file.Path.Value, [file]));
                return node;
            })
            .ToArray();

        ExplorerTree.ItemsSource = nodes.Select(CreateTreeItem).ToArray();
        UpdateCapabilities();
    }

    private void PivotDirectory_Click(object? sender, RoutedEventArgs e)
    {
        var nodes = SelectedNodesFromActiveProjection();
        if (nodes.Count != 1) return;
        var scope = InferSelectedScope(nodes[0]);
        if (scope is null) return;
        ShowScopeProjection(scope.Value, includeDescendants: false, "Directory");
    }

    private void PivotBranch_Click(object? sender, RoutedEventArgs e)
    {
        var nodes = SelectedNodesFromActiveProjection();
        if (nodes.Count != 1) return;
        var scope = InferSelectedScope(nodes[0]);
        if (scope is null) return;
        ShowScopeProjection(scope.Value, includeDescendants: true, "Branch");
    }

    private FileSystemPath? InferSelectedScope(ExplorerNode node)
    {
        var files = DistinctFiles(node.Files);
        if (files.Count == 0) return null;
        if (files.Count == 1) return files[0].ParentDirectory;

        // Directory/branch nodes contain all duplicate descendants. Walk the first
        // file upward and choose the narrowest ancestor that contains every selected
        // descendant and whose name (or full path for a root node) matches the node.
        FileSystemPath? candidate = files[0].ParentDirectory;
        while (candidate is not null)
        {
            var path = candidate.Value;
            var labelMatches = string.Equals(path.Value, node.Label, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path.Value), node.Label, StringComparison.OrdinalIgnoreCase);
            var containsAll = files.All(file =>
                fileSystem.PathsEqual(file.ParentDirectory, path)
                || fileSystem.IsDescendant(file.ParentDirectory, path));
            if (labelMatches && containsAll)
                return path;
            candidate = fileSystem.GetParentDirectory(path);
        }

        return null;
    }

    private void ShowScopeProjection(FileSystemPath scope, bool includeDescendants, string title)
    {
        var session = controller.Session;
        if (session is null) return;

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
                .ToArray();
            var root = new ExplorerNode(scope.Value, files);
            foreach (var file in files)
                root.Children.Add(new ExplorerNode(Path.GetFileName(file.Path.Value), [file]));
            ExplorerTree.ItemsSource = new[] { CreateTreeItem(root) };
        }

        UpdateCapabilities();
    }

    private void UpdateRootsCancelCapability() =>
        CancelRootsButton.IsEnabled = controller.Session is not null;
}
