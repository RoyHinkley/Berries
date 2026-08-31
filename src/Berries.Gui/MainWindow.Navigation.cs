using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Berries.Core;
using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private void ExploreButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not null && CorpusRootsMatchCurrentSelection())
        {
            RootsPanel.IsVisible = false;
            ExplorerPanel.IsVisible = true;
            StatusText.Text = "Returned to the current session.";
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            return;
        }
        ScanButton_Click(sender, e);
    }

    private bool CorpusRootsMatchCurrentSelection()
    {
        var corpus = controller.Corpus;
        return corpus is not null
            && Projections.CorpusRootsMatch(corpus, roots.Select(root => new FileSystemPath(root)));
    }

    private void ExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!synchronizingSelection) UpdateCapabilities();
    }

    private void UpdatePivotCapabilities()
    {
        var scope = SelectedScope();
        PivotCorpusRootsMenu.IsEnabled = controller.Session is not null;
        PivotContentMenu.IsEnabled = controller.Session is not null;
        PivotDirectoryMenu.IsEnabled = scope is not null;
        PivotBranchMenu.IsEnabled = scope is not null;
        PivotBestDirectoryPairMenu.IsEnabled = scope is not null;
        var branches = controller.BranchStatistics?.Branches;
        PivotBestBranchPairMenu.IsEnabled = scope is not null
            && branches is not null
            && Projections.HasBranchPairCandidate(branches, scope.Value);
        var suggestions = controller.Suggestions?.Suggestions;
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private void UpdateSelectionStatus() => UpdateSelectionSummary();

    private async void PivotCorpusRoots_Click(object? sender, RoutedEventArgs e)
    {
        var corpus = controller.Corpus;
        var session = controller.Session;
        if (session is null || corpus is null) return;
        BeginProgress("Building Corpus Roots view...", true);
        try
        {
            var projections = await Projections.CorpusRootsAsync(session, corpus);
            var nodes = projections.Select(projection => BuildBranchExplorerNode(projection.Root)).ToArray();
            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.CorpusRoots, nodes.SelectMany(node => node.Files));
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = "Corpus Roots";
            ExplorerTree.ItemsSource = nodes;
            EndProgress("Corpus Roots");
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
        }
        catch (Exception ex)
        {
            EndProgress("Could not build Corpus Roots view: " + ex.Message);
        }
    }

    private async void PivotSelectedContent_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session;
        if (session is null) return;
        BeginProgress("Building selected Groups view...", true);
        try
        {
            var groups = await Projections.GroupsForSelectionAsync(
                session,
                new Progress<OperationProgress>(ShowAnalysisProgress));
            if (groups.Count == 0)
            {
                await ShowContentProjectionAsync();
                return;
            }

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.Groups, groups.SelectMany(group => group.Files));
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = groups.Count == 1 ? "Group" : $"Groups — {groups.Count:N0} selected";
            ExplorerTree.ItemsSource = groups.Select(BuildGroupNode).ToArray();
            EndProgress(ProjectionTitle.Text ?? "Groups");
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
        }
        catch (Exception ex)
        {
            EndProgress("Could not build Groups view: " + ex.Message);
        }
    }

    private static ExplorerNode BuildGroupNode(GroupProjection projection)
    {
        var node = new ExplorerNode(projection.Label, projection.Files);
        foreach (var item in projection.Items)
            node.Children.Add(new ExplorerNode(item.Label, [item.File], item.File.ParentDirectory));
        return node;
    }

    private ExplorerNode BuildGroupNode(IReadOnlyList<FileInstance> files) =>
        BuildGroupNode(Projections.Group(files));

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
        var scope = SelectedScope();
        if (scope is null) return;
        var pairs = controller.DirectoryAnalysis?.DirectoryPairs;
        var pair = pairs is null ? null : Projections.BestDirectoryPair(pairs, scope.Value);
        if (pair is not null)
        {
            await ShowDirectoryPairProjectionAsync(pair);
            return;
        }

        var directories = controller.DirectoryAnalysis?.Directories;
        var record = directories is null ? null : Projections.DirectoryRecord(directories, scope.Value);
        StatusText.Text = record is null || record.GroupedFileCount == 0
            ? "The selected Directory contains no grouped files."
            : "The selected Directory contains Groups, but shares none with another Directory.";
        StatusProgress.IsVisible = false;
    }

    private async void PivotBestBranchPair_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope();
        if (scope is null) return;
        try
        {
            var pair = await controller.FindBestBranchPairAsync(scope.Value);
            if (pair is null)
            {
                EndProgress("No Branch Pair shares a Group with the selected Branch.");
                return;
            }
            await ShowAdHocBranchPairAsync(pair.First, pair.Second, pair.SharedGroupCount);
            EndProgress($"Best Branch Pair — {pair.SharedGroupCount:N0} shared Groups.");
        }
        catch (Exception ex)
        {
            EndProgress("Best Branch Pair search failed: " + ex.Message);
        }
    }

    private FileSystemPath? SelectedScope() => focusedNode?.SemanticPath ?? currentProjection?.Primary;

    private async Task ShowAdHocBranchPairAsync(
        FileSystemPath first,
        FileSystemPath second,
        int sharedGroupCount)
    {
        BeginProgress("Opening Branch Pair...", true);
        try
        {
            var leftTask = BuildBranchExplorerNodeAsync(first);
            var rightTask = BuildBranchExplorerNodeAsync(second);
            await Task.WhenAll(leftTask, rightTask);
            var left = await leftTask;
            var right = await rightTask;
            PairExplorer.IsVisible = true;
            SingleExplorer.IsVisible = false;
            SetPairProjectionState(ProjectionKind.BranchPair, first, left.Files, second, right.Files);
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = $"Branch Pair — {sharedGroupCount:N0} shared Groups";
            BuildPairBreadcrumbs(first, LeftScopeBreadcrumbs, true, "Branch", PairSide.Left);
            BuildPairBreadcrumbs(second, RightScopeBreadcrumbs, true, "Branch", PairSide.Right);
            LeftTree.ItemsSource = new[] { left };
            RightTree.ItemsSource = new[] { right };
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
        }
        finally
        {
            StatusProgress.IsVisible = false;
        }
    }

    private async void Suggest_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Suggestions?.Suggestions;
        if (suggestions is null || suggestions.Count == 0) return;
        suggestionIndex = (suggestionIndex + 1) % suggestions.Count;
        await ShowBranchPairAsync(suggestions[suggestionIndex]);
        RebuildCurrentPairBreadcrumbs();
        SynchronizeVisibleSelection();
        UpdateSelectionSummary();
    }

    private async void PivotSuggestedBranchPair_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Suggestions?.Suggestions;
        if (suggestions is null || suggestionIndex < 0 || suggestionIndex >= suggestions.Count) return;
        await ShowBranchPairAsync(suggestions[suggestionIndex]);
        RebuildCurrentPairBreadcrumbs();
        SynchronizeVisibleSelection();
        UpdateSelectionSummary();
    }

    private void RebuildCurrentPairBreadcrumbs()
    {
        if (currentProjection is not { Kind: ProjectionKind.BranchPair, Primary: { } first, Secondary: { } second })
            return;
        BuildPairBreadcrumbs(first, LeftScopeBreadcrumbs, true, "Branch", PairSide.Left);
        BuildPairBreadcrumbs(second, RightScopeBreadcrumbs, true, "Branch", PairSide.Right);
    }

    private void BuildBreadcrumbs(FileSystemPath path)
    {
        var isBranch = currentProjection?.Kind == ProjectionKind.Branch;
        BuildBreadcrumbs(path, BreadcrumbPanel, isBranch, isBranch ? "Branch" : "Directory", null);
        BreadcrumbPanel.IsVisible = BreadcrumbPanel.Children.Count > 0;
    }

    private void BuildPairBreadcrumbs(FileSystemPath path, StackPanel panel, bool includeDescendants, string title, PairSide side) =>
        BuildBreadcrumbs(path, panel, includeDescendants, title, side);

    private void BuildBreadcrumbs(
        FileSystemPath path,
        StackPanel panel,
        bool includeDescendants,
        string title,
        PairSide? side)
    {
        panel.Children.Clear();
        var corpus = controller.Corpus;
        if (corpus is null) return;
        var chain = Projections.Breadcrumbs(corpus, path);
        for (var i = 0; i < chain.Count; i++)
        {
            if (i > 0)
                panel.Children.Add(new TextBlock
                {
                    Text = "›",
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
            var item = chain[i];
            var button = new Button
            {
                Content = i == 0 ? item.Value : Path.GetFileName(item.Value),
                Padding = new Avalonia.Thickness(4, 1),
                Tag = new BreadcrumbTarget(item, includeDescendants, title, side),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            button.Click += Breadcrumb_Click;
            panel.Children.Add(button);
        }
    }

    private async void Breadcrumb_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BreadcrumbTarget target }) return;
        if (target.Side is null)
        {
            if (target.IncludeDescendants)
                await ShowBranchProjectionAsync(target.Path);
            else
                await ShowDirectoryProjectionAsync(target.Path);
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
        if (session is null
            || currentProjection is not { Primary: { } primary, Secondary: { } secondary }
            || target.Side is null)
            return;

        var side = target.Side.Value;
        var first = side == PairSide.Left ? target.Path : primary;
        var second = side == PairSide.Right ? target.Path : secondary;
        BeginProgress($"Opening {target.Title}...", true);
        try
        {
            var nodeTask = BuildBranchExplorerNodeAsync(target.Path);
            var sharedTask = Projections.SharedGroupCountAsync(
                session,
                first,
                second,
                includeDescendants: true);
            await Task.WhenAll(nodeTask, sharedTask);
            var node = await nodeTask;

            if (side == PairSide.Left)
            {
                LeftTree.ItemsSource = new[] { node };
                BuildPairBreadcrumbs(target.Path, LeftScopeBreadcrumbs, true, "Branch", PairSide.Left);
            }
            else
            {
                RightTree.ItemsSource = new[] { node };
                BuildPairBreadcrumbs(target.Path, RightScopeBreadcrumbs, true, "Branch", PairSide.Right);
            }

            UpdatePairProjectionSide(side, target.Path, node.Files);
            var shared = await sharedTask;
            ProjectionTitle.Text = $"Branch Pair — {shared:N0} shared Groups";
            EndProgress($"Branch Pair — {shared:N0} shared Groups.");
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
        }
        catch (Exception ex)
        {
            EndProgress("Could not open Branch: " + ex.Message);
        }
    }

    private enum PairSide { Left, Right }

    private sealed record BreadcrumbTarget(
        FileSystemPath Path,
        bool IncludeDescendants,
        string Title,
        PairSide? Side);
}
