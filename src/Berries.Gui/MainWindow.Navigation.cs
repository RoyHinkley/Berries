using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

public partial class MainWindow
{
    private bool scopeIncludesDescendants;
    private string scopeProjectionTitle = "Directory";
    private FileSystemPath? currentScope;

    private void ExploreButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not null && CorpusRootsMatchCurrentSelection())
        {
            RootsPanel.IsVisible = false; ExplorerPanel.IsVisible = true;
            StatusText.Text = "Returned to the current session."; return;
        }
        ScanButton_Click(sender, e);
    }

    private bool CorpusRootsMatchCurrentSelection()
    {
        var corpus = controller.Corpus;
        if (corpus is null || corpus.Roots.Count != roots.Count) return false;
        return roots.All(root => corpus.Roots.Any(existing => fileSystem.PathsEqual(existing.Path, new FileSystemPath(root))));
    }

    private void ExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = sender is TreeView tree && tree.SelectedItems is { Count: > 0 };
        InvertButton.IsEnabled = hasSelection; ExcludeButton.IsEnabled = hasSelection; DeleteButton.IsEnabled = hasSelection;
    }

    private void UpdatePivotCapabilities()
    {
        var scope = SelectedScope();
        PivotCorpusRootsMenu.IsEnabled = controller.Session is not null;
        PivotContentMenu.IsEnabled = controller.Session is not null;
        PivotDirectoryMenu.IsEnabled = scope is not null; PivotBranchMenu.IsEnabled = scope is not null;
        PivotBestDirectoryPairMenu.IsEnabled = scope is not null;
        PivotBestBranchPairMenu.IsEnabled = scope is not null && HasBranchPairCandidate(scope.Value);
        var suggestions = controller.Counterparts?.Seeds;
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private bool HasBranchPairCandidate(FileSystemPath scope)
    {
        var branches = controller.BranchStatistics?.Branches; if (branches is null) return false;
        return branches.Any(branch => fileSystem.PathsEqual(branch.Path, scope) && branch.DuplicateContentCount > 0)
            && branches.Any(branch => !fileSystem.PathsEqual(branch.Path, scope) && !fileSystem.IsDescendant(branch.Path, scope)
                && !fileSystem.IsDescendant(scope, branch.Path) && branch.DuplicateContentCount > 0);
    }

    private void UpdateSelectionStatus()
    {
        if (StatusProgress.IsVisible) return;
        var nodes = SelectedNodesFromActiveProjection(); if (nodes.Count == 0) return;
        var files = DistinctFiles(nodes.SelectMany(node => node.Files));
        var groups = files.Where(file => file.Content is not null).Select(file => file.Content!.Value).Distinct().Count();
        if (files.Count == 1) StatusText.Text = $"{files[0].Path.Value} — {files[0].Length:N0} bytes";
        else if (files.Count > 0) StatusText.Text = $"Selection — {files.Count:N0} files, {groups:N0} Groups";
        else StatusText.Text = nodes.Count == 1 ? nodes[0].Label : $"Selection — {nodes.Count:N0} items";
    }

    private IReadOnlyList<ExplorerNode> SelectedNodesFromActiveProjection()
    {
        IEnumerable<object> selected = PairExplorer.IsVisible ? SelectedObjects(LeftTree).Concat(SelectedObjects(RightTree)) : SelectedObjects(ExplorerTree);
        return selected.OfType<ExplorerNode>().Distinct().ToArray();
    }

    private static IEnumerable<object> SelectedObjects(TreeView tree) => tree.SelectedItems?.Cast<object>() ?? [];

    private async void PivotCorpusRoots_Click(object? sender, RoutedEventArgs e)
    {
        var corpus = controller.Corpus; if (controller.Session is null || corpus is null) return;
        BeginProgress("Building Corpus Roots view...", true);
        try
        {
            var nodes = await Task.Run(() => corpus.Roots.Select(root => BuildBranchTree(root.Path)).ToArray());
            currentScope = null; leftScope = null; rightScope = null; PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
            BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear(); ProjectionTitle.Text = "Corpus Roots";
            ExplorerTree.ItemsSource = nodes; EndProgress("Corpus Roots"); UpdateCapabilities();
        }
        catch (Exception ex) { EndProgress("Could not build Corpus Roots view: " + ex.Message); }
    }

    private void PivotSelectedContent_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session; if (session is null) return;
        var selected = DistinctFiles(SelectedNodesFromActiveProjection().SelectMany(node => node.Files));
        var contentIds = selected.Where(file => file.Content is not null).Select(file => file.Content!.Value).Distinct().ToHashSet();
        if (contentIds.Count == 0) { ShowContentProjection(); return; }
        currentScope = null; leftScope = null; rightScope = null; PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
        BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear();
        ProjectionTitle.Text = contentIds.Count == 1 ? "Group" : $"Groups — {contentIds.Count:N0} selected";
        ExplorerTree.ItemsSource = session.DuplicateSets.Where(set => contentIds.Contains(set.Content)).OrderByDescending(set => set.Files.Count)
            .Select(set => BuildGroupNode(set.Files)).ToArray(); UpdateCapabilities();
    }

    private ExplorerNode BuildGroupNode(IReadOnlyList<FileInstance> files)
    {
        var names = files.Select(file => Path.GetFileName(file.Path.Value)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var shownNames = string.Join(", ", names.Take(2)); if (names.Length > 2) shownNames += ", …";
        var node = new ExplorerNode($"{shownNames} — {files.Count:N0} files", files);
        foreach (var file in files.OrderBy(file => file.Path.Value, StringComparer.OrdinalIgnoreCase)) node.Children.Add(new ExplorerNode(file.Path.Value, [file]));
        return node;
    }

    private void PivotDirectory_Click(object? sender, RoutedEventArgs e) { var scope = SelectedScope(); if (scope is not null) ShowScopeProjection(scope.Value, false, "Directory"); }
    private void PivotBranch_Click(object? sender, RoutedEventArgs e) { var scope = SelectedScope(); if (scope is not null) ShowScopeProjection(scope.Value, true, "Branch"); }

    private void PivotBestDirectoryPair_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope(); if (scope is null) return;
        var pair = FindBestDirectoryPair(scope.Value); if (pair is not null) { ShowDirectoryPair(pair); return; }
        var record = controller.DirectoryAnalysis?.Directories.FirstOrDefault(directory => fileSystem.PathsEqual(directory.Path, scope.Value));
        StatusText.Text = record is null || record.DuplicateFileCount == 0 ? "The selected Directory contains no duplicate files."
            : "The selected Directory has duplicate files, but none shared with another Directory.";
        StatusProgress.IsVisible = false;
    }

    private async void PivotBestBranchPair_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope(); if (scope is null) return;
        try
        {
            var pair = await controller.FindBestBranchPairAsync(scope.Value);
            if (pair is null) { EndProgress("No Branch Pair shares duplicate content with the selected Branch."); return; }
            ShowAdHocBranchPair(pair.First, pair.Second, pair.SharedDuplicateContentCount);
            EndProgress($"Best Branch Pair — {pair.SharedDuplicateContentCount:N0} shared Groups.");
        }
        catch (Exception ex) { EndProgress("Best Branch Pair search failed: " + ex.Message); }
    }

    private FileSystemPath? SelectedScope()
    {
        var nodes = SelectedNodesFromActiveProjection(); if (nodes.Count == 1) return InferSelectedScope(nodes[0]);
        return nodes.Count == 0 ? currentScope : null;
    }

    private FileSystemPath? InferSelectedScope(ExplorerNode node)
    {
        var files = DistinctFiles(node.Files); if (files.Count == 0) return null; if (files.Count == 1) return files[0].ParentDirectory;
        FileSystemPath? candidate = files[0].ParentDirectory;
        while (candidate is not null)
        {
            var path = candidate.Value;
            var labelMatches = string.Equals(path.Value, node.Label, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(path.Value), node.Label, StringComparison.OrdinalIgnoreCase);
            var containsAll = files.All(file => fileSystem.PathsEqual(file.ParentDirectory, path) || fileSystem.IsDescendant(file.ParentDirectory, path));
            if (labelMatches && containsAll) return path; candidate = fileSystem.GetParentDirectory(path);
        }
        return null;
    }

    private DirectoryPair? FindBestDirectoryPair(FileSystemPath scope) => controller.DirectoryAnalysis?.DirectoryPairs
        .Where(pair => fileSystem.PathsEqual(pair.First, scope) || fileSystem.PathsEqual(pair.Second, scope)).OrderByDescending(pair => pair.SharedContentCount).FirstOrDefault();

    private void ShowDirectoryPair(DirectoryPair pair)
    {
        currentScope = null; leftScope = pair.First; rightScope = pair.Second; PairExplorer.IsVisible = true; SingleExplorer.IsVisible = false;
        BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear(); ProjectionTitle.Text = $"Directory Pair — {pair.SharedContentCount:N0} shared Groups";
        BuildPairBreadcrumbs(pair.First, LeftScopeBreadcrumbs, false, "Directory"); BuildPairBreadcrumbs(pair.Second, RightScopeBreadcrumbs, false, "Directory");
        LeftTree.ItemsSource = new[] { BuildDirectoryTree(pair.First) }; RightTree.ItemsSource = new[] { BuildDirectoryTree(pair.Second) }; UpdateCapabilities();
    }

    private ExplorerNode BuildDirectoryTree(FileSystemPath scope)
    {
        var session = controller.Session ?? throw new InvalidOperationException("No session.");
        var files = session.DuplicateSets.SelectMany(set => set.Files).Where(file => fileSystem.PathsEqual(file.ParentDirectory, scope))
            .OrderBy(file => file.Path.Value, StringComparer.OrdinalIgnoreCase).ToArray();
        var root = new ExplorerNode(scope.Value, files); foreach (var file in files) root.Children.Add(new ExplorerNode(Path.GetFileName(file.Path.Value), [file])); return root;
    }

    private void ShowAdHocBranchPair(FileSystemPath first, FileSystemPath second, int sharedContentCount)
    {
        currentScope = null; leftScope = first; rightScope = second; PairExplorer.IsVisible = true; SingleExplorer.IsVisible = false;
        BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear(); ProjectionTitle.Text = $"Branch Pair — {sharedContentCount:N0} shared Groups";
        BuildPairBreadcrumbs(first, LeftScopeBreadcrumbs, true, "Branch"); BuildPairBreadcrumbs(second, RightScopeBreadcrumbs, true, "Branch");
        LeftTree.ItemsSource = new[] { BuildBranchTree(first) }; RightTree.ItemsSource = new[] { BuildBranchTree(second) }; UpdateCapabilities();
    }

    private void SuggestCaseWithBreadcrumbs_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds; if (suggestions is null || suggestions.Count == 0) return;
        suggestionIndex = (suggestionIndex + 1) % suggestions.Count; ShowBranchPair(suggestions[suggestionIndex]);
        if (leftScope is not null) BuildPairBreadcrumbs(leftScope.Value, LeftScopeBreadcrumbs, true, "Branch");
        if (rightScope is not null) BuildPairBreadcrumbs(rightScope.Value, RightScopeBreadcrumbs, true, "Branch");
    }

    private void PivotSuggestedBranchPairWithBreadcrumbs_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds; if (suggestions is null || suggestionIndex < 0 || suggestionIndex >= suggestions.Count) return;
        ShowBranchPair(suggestions[suggestionIndex]);
        if (leftScope is not null) BuildPairBreadcrumbs(leftScope.Value, LeftScopeBreadcrumbs, true, "Branch");
        if (rightScope is not null) BuildPairBreadcrumbs(rightScope.Value, RightScopeBreadcrumbs, true, "Branch");
    }

    private void ShowScopeProjection(FileSystemPath scope, bool includeDescendants, string title)
    {
        if (controller.Session is null) return;
        currentScope = scope; scopeIncludesDescendants = includeDescendants; scopeProjectionTitle = title; leftScope = null; rightScope = null;
        PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true; ProjectionTitle.Text = title; BuildBreadcrumbs(scope);
        ExplorerTree.ItemsSource = includeDescendants ? new[] { BuildBranchTree(scope) } : new[] { BuildDirectoryTree(scope) };
        UpdateCapabilities(); UpdatePivotCapabilities();
    }

    private void BuildBreadcrumbs(FileSystemPath scope)
    {
        BuildBreadcrumbs(scope, BreadcrumbPanel, scopeIncludesDescendants, scopeProjectionTitle);
        BreadcrumbPanel.IsVisible = BreadcrumbPanel.Children.Count > 0;
    }

    private void BuildPairBreadcrumbs(FileSystemPath scope, StackPanel panel, bool includeDescendants, string title) => BuildBreadcrumbs(scope, panel, includeDescendants, title);

    private void BuildBreadcrumbs(FileSystemPath scope, StackPanel panel, bool includeDescendants, string title)
    {
        panel.Children.Clear(); var root = CorpusRootFor(scope); if (root is null) return;
        var chain = new List<FileSystemPath>(); var current = scope;
        while (true)
        {
            chain.Add(current); if (fileSystem.PathsEqual(current, root.Value)) break;
            var parent = fileSystem.GetParentDirectory(current); if (parent is null) break; current = parent.Value;
        }
        chain.Reverse();
        for (var i = 0; i < chain.Count; i++)
        {
            if (i > 0) panel.Children.Add(new TextBlock { Text = "›", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var path = chain[i];
            var button = new Button
            {
                Content = i == 0 ? path.Value : Path.GetFileName(path.Value), Padding = new Avalonia.Thickness(4, 1),
                Tag = new BreadcrumbTarget(path, includeDescendants, title), Cursor = new Cursor(StandardCursorType.Hand)
            };
            button.Click += Breadcrumb_Click; panel.Children.Add(button);
        }
    }

    private void Breadcrumb_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BreadcrumbTarget target }) ShowScopeProjection(target.Path, target.IncludeDescendants, target.Title);
    }

    private FileSystemPath? CorpusRootFor(FileSystemPath path)
    {
        var corpus = controller.Corpus; if (corpus is null) return null;
        foreach (var root in corpus.Roots.Select(item => item.Path)) if (fileSystem.PathsEqual(path, root) || fileSystem.IsDescendant(path, root)) return root;
        return null;
    }

    private sealed record BreadcrumbTarget(FileSystemPath Path, bool IncludeDescendants, string Title);
}
