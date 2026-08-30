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
            StatusText.Text = "Returned to the current session.";
            SynchronizeVisibleSelection(); UpdateSelectionSummary(); return;
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
        if (synchronizingSelection) return;
        UpdateCapabilities();
    }

    private void UpdatePivotCapabilities()
    {
        var scope = SelectedScope();
        PivotCorpusRootsMenu.IsEnabled = controller.Session is not null;
        PivotContentMenu.IsEnabled = controller.Session is not null;
        PivotDirectoryMenu.IsEnabled = scope is not null; PivotBranchMenu.IsEnabled = scope is not null;
        PivotBestDirectoryPairMenu.IsEnabled = scope is not null;
        var branches = controller.BranchStatistics?.Branches;
        PivotBestBranchPairMenu.IsEnabled = scope is not null && branches is not null
            && Projections.HasBranchPairCandidate(branches, scope.Value);
        var suggestions = controller.Counterparts?.Seeds;
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private void UpdateSelectionStatus() => UpdateSelectionSummary();

    private IReadOnlyList<ExplorerNode> SelectedNodesFromActiveProjection()
    {
        if (focusedNode is not null) return [focusedNode];
        return [];
    }

    private static IEnumerable<object> SelectedObjects(TreeView tree) => tree.SelectedItems?.Cast<object>() ?? [];

    private async void PivotCorpusRoots_Click(object? sender, RoutedEventArgs e)
    {
        var corpus = controller.Corpus; if (controller.Session is null || corpus is null) return;
        BeginProgress("Building Corpus Roots view...", true);
        try
        {
            var tasks = corpus.Roots.Select(root => BuildBranchExplorerNodeAsync(root.Path)).ToArray();
            await Task.WhenAll(tasks);
            var nodes = tasks.Select(task => task.Result).ToArray();
            currentScope = null; leftScope = null; rightScope = null; PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
            BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear(); ProjectionTitle.Text = "Corpus Roots";
            ExplorerTree.ItemsSource = nodes; EndProgress("Corpus Roots"); SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
        }
        catch (Exception ex) { EndProgress("Could not build Corpus Roots view: " + ex.Message); }
    }

    private void PivotSelectedContent_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session; if (session is null) return;
        var selected = session.Selection.Files;
        var contentIds = selected.Where(file => file.Content is not null).Select(file => file.Content!.Value).Distinct().ToHashSet();
        if (contentIds.Count == 0) { ShowContentProjection(); return; }
        currentScope = null; leftScope = null; rightScope = null; PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
        BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear();
        ProjectionTitle.Text = contentIds.Count == 1 ? "Group" : $"Groups — {contentIds.Count:N0} selected";
        ExplorerTree.ItemsSource = session.DuplicateSets.Where(set => contentIds.Contains(set.Content)).OrderByDescending(set => set.Files.Count)
            .Select(set => BuildGroupNode(set.Files)).ToArray();
        SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
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

    private async void PivotDirectory_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope();
        if (scope is not null) await ShowDirectoryProjectionAsync(scope.Value);
    }

    private async void PivotBranch_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope();
        if (scope is not null) await ShowBranchProjectionAsync(scope.Value);
    }

    private async void PivotBestDirectoryPair_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope(); if (scope is null) return;
        var pairs = controller.DirectoryAnalysis?.DirectoryPairs;
        var pair = pairs is null ? null : Projections.BestDirectoryPair(pairs, scope.Value);
        if (pair is not null)
        {
            await ShowDirectoryPairProjectionAsync(pair);
            return;
        }
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
            await ShowAdHocBranchPairAsync(pair.First, pair.Second, pair.SharedDuplicateContentCount);
            EndProgress($"Best Branch Pair — {pair.SharedDuplicateContentCount:N0} shared Groups.");
        }
        catch (Exception ex) { EndProgress("Best Branch Pair search failed: " + ex.Message); }
    }

    private FileSystemPath? SelectedScope()
    {
        if (focusedNode is not null) return InferSelectedScope(focusedNode);
        return currentScope;
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

    private async Task ShowAdHocBranchPairAsync(FileSystemPath first, FileSystemPath second, int sharedContentCount)
    {
        BeginProgress("Opening Branch Pair...", true);
        try
        {
            var leftTask = BuildBranchExplorerNodeAsync(first);
            var rightTask = BuildBranchExplorerNodeAsync(second);
            await Task.WhenAll(leftTask, rightTask);
            currentScope = null; leftScope = first; rightScope = second; PairExplorer.IsVisible = true; SingleExplorer.IsVisible = false;
            BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear(); ProjectionTitle.Text = $"Branch Pair — {sharedContentCount:N0} shared Groups";
            BuildPairBreadcrumbs(first, LeftScopeBreadcrumbs, true, "Branch", PairSide.Left); BuildPairBreadcrumbs(second, RightScopeBreadcrumbs, true, "Branch", PairSide.Right);
            LeftTree.ItemsSource = new[] { await leftTask }; RightTree.ItemsSource = new[] { await rightTask };
            SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
        }
        finally { StatusProgress.IsVisible = false; }
    }

    private async void SuggestCaseWithBreadcrumbs_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds; if (suggestions is null || suggestions.Count == 0) return;
        suggestionIndex = (suggestionIndex + 1) % suggestions.Count; await ShowBranchPairAsync(suggestions[suggestionIndex]);
        if (leftScope is not null) BuildPairBreadcrumbs(leftScope.Value, LeftScopeBreadcrumbs, true, "Branch", PairSide.Left);
        if (rightScope is not null) BuildPairBreadcrumbs(rightScope.Value, RightScopeBreadcrumbs, true, "Branch", PairSide.Right);
        SynchronizeVisibleSelection(); UpdateSelectionSummary();
    }

    private async void PivotSuggestedBranchPairWithBreadcrumbs_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds; if (suggestions is null || suggestionIndex < 0 || suggestionIndex >= suggestions.Count) return;
        await ShowBranchPairAsync(suggestions[suggestionIndex]);
        if (leftScope is not null) BuildPairBreadcrumbs(leftScope.Value, LeftScopeBreadcrumbs, true, "Branch", PairSide.Left);
        if (rightScope is not null) BuildPairBreadcrumbs(rightScope.Value, RightScopeBreadcrumbs, true, "Branch", PairSide.Right);
        SynchronizeVisibleSelection(); UpdateSelectionSummary();
    }

    private void BuildBreadcrumbs(FileSystemPath scope)
    {
        BuildBreadcrumbs(scope, BreadcrumbPanel, scopeIncludesDescendants, scopeProjectionTitle, null);
        BreadcrumbPanel.IsVisible = BreadcrumbPanel.Children.Count > 0;
    }

    private void BuildPairBreadcrumbs(FileSystemPath scope, StackPanel panel, bool includeDescendants, string title, PairSide side) =>
        BuildBreadcrumbs(scope, panel, includeDescendants, title, side);

    private void BuildBreadcrumbs(FileSystemPath scope, StackPanel panel, bool includeDescendants, string title, PairSide? side)
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
                Tag = new BreadcrumbTarget(path, includeDescendants, title, side), Cursor = new Cursor(StandardCursorType.Hand)
            };
            button.Click += Breadcrumb_Click; panel.Children.Add(button);
        }
    }

    private async void Breadcrumb_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BreadcrumbTarget target }) return;
        if (target.Side is null)
        {
            if (target.IncludeDescendants) await ShowBranchProjectionAsync(target.Path);
            else await ShowDirectoryProjectionAsync(target.Path);
            return;
        }
        if (!target.IncludeDescendants)
        {
            await NavigateDirectoryPairBreadcrumbAsync(target);
            return;
        }
        await NavigatePairBreadcrumbAsync(target);
    }

    private async Task NavigatePairBreadcrumbAsync(BreadcrumbTarget target)
    {
        var session = controller.Session;
        if (session is null || leftScope is null || rightScope is null || target.Side is null) return;

        var side = target.Side.Value;
        var first = side == PairSide.Left ? target.Path : leftScope.Value;
        var second = side == PairSide.Right ? target.Path : rightScope.Value;
        BeginProgress($"Opening {target.Title}...", true);
        try
        {
            var nodeTask = BuildBranchExplorerNodeAsync(target.Path);
            var sharedTask = Projections.SharedGroupCountAsync(session, first, second, includeDescendants: true);
            await Task.WhenAll(nodeTask, sharedTask);

            if (side == PairSide.Left)
            {
                leftScope = target.Path;
                LeftTree.ItemsSource = new[] { await nodeTask };
                BuildPairBreadcrumbs(target.Path, LeftScopeBreadcrumbs, true, "Branch", PairSide.Left);
            }
            else
            {
                rightScope = target.Path;
                RightTree.ItemsSource = new[] { await nodeTask };
                BuildPairBreadcrumbs(target.Path, RightScopeBreadcrumbs, true, "Branch", PairSide.Right);
            }

            var shared = await sharedTask;
            ProjectionTitle.Text = $"Branch Pair — {shared:N0} shared Groups";
            EndProgress($"Branch Pair — {shared:N0} shared Groups.");
            SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities(); UpdatePivotCapabilities();
        }
        catch (Exception ex)
        {
            EndProgress("Could not open Branch: " + ex.Message);
        }
    }

    private FileSystemPath? CorpusRootFor(FileSystemPath path)
    {
        var corpus = controller.Corpus; if (corpus is null) return null;
        foreach (var root in corpus.Roots.Select(item => item.Path)) if (fileSystem.PathsEqual(path, root) || fileSystem.IsDescendant(path, root)) return root;
        return null;
    }

    private enum PairSide { Left, Right }
    private sealed record BreadcrumbTarget(FileSystemPath Path, bool IncludeDescendants, string Title, PairSide? Side);
}
