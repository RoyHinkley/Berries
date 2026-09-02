using System.Collections.ObjectModel;
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
        var session = controller.Session;
        var containingDirectory = ContainingDirectoryScope();
        var branch = BranchScope();
        var directoryPairSeed = DirectoryPairSeed();
        var branchPairSeed = BranchPairSeed();

        PivotCorpusRootsMenu.IsEnabled = session is not null
            && currentProjection?.Kind != ProjectionKind.CorpusRoots;
        PivotContentMenu.IsEnabled = session is not null
            && (!session.Selection.IsEmpty || currentProjection?.Kind != ProjectionKind.Groups);
        PivotDirectoryMenu.IsEnabled = containingDirectory is not null
            && !IsCurrentProjection(ProjectionKind.Directory, containingDirectory.Value);
        PivotBranchMenu.IsEnabled = branch is not null
            && !IsCurrentProjection(ProjectionKind.Branch, branch.Value);
        PivotBestDirectoryPairMenu.IsEnabled = directoryPairSeed is not null;

        var branches = controller.BranchStatistics?.Branches;
        PivotBestBranchPairMenu.IsEnabled = branchPairSeed is not null
            && branches is not null
            && Projections.HasBranchPairCandidate(branches, branchPairSeed.Value);

        var suggestions = controller.Suggestions?.Suggestions;
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private void UpdateSelectionStatus() => UpdateSelectionSummary();

    private async void PivotCorpusRoots_Click(object? sender, RoutedEventArgs e)
    {
        var corpus = controller.Corpus;
        var session = controller.Session;
        if (session is null || corpus is null) return;

        var operation = BeginNavigation("Building Roots view...", true);
        try
        {
            var projections = await Projections.CorpusRootsAsync(
                session,
                corpus,
                new Progress<OperationProgress>(progress => ShowNavigationProgress(operation, progress)),
                operation.Token);
            operation.Mark("Roots projection acquired");
            operation.Token.ThrowIfCancellationRequested();
            var nodes = await Task.Run(
                () => projections.Select(projection => BuildBranchExplorerNode(projection.Root)).ToArray(),
                operation.Token);
            operation.Mark($"Roots GUI nodes built ({nodes.Length:N0} roots)");
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.CorpusRoots, nodes.SelectMany(node => node.Files));
            operation.Mark("Roots projection state set");
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = "Roots";
            ExplorerTree.ItemsSource = nodes;
            operation.Mark("Roots ItemsSource assigned");
            SynchronizeVisibleSelection();
            operation.Mark("Roots selection synchronized");
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            operation.Mark("Roots capabilities updated");
            operation.MarkWhenUiSettled("Roots UI reached Background priority");
            CompleteNavigation(operation, "Roots");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not build Roots view: " + ex.Message);
        }
    }

    private async void PivotSelectedContent_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session;
        if (session is null) return;

        var operation = BeginNavigation("Building selected Groups view...", true);
        try
        {
            var groups = await Projections.GroupsForSelectionAsync(
                session,
                new Progress<OperationProgress>(progress => ShowNavigationProgress(operation, progress)),
                operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            if (groups.Count == 0)
            {
                RetireNavigation(operation);
                await ShowContentProjectionAsync();
                return;
            }

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.Groups, groups.SelectMany(group => group.Files));
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = groups.Count == 1 ? "Group" : $"Groups — {groups.Count:N0} selected";

            var nodes = new ObservableCollection<ExplorerNode>();
            ExplorerTree.ItemsSource = nodes;
            await Task.Yield();
            await BuildGroupsExplorerTreeAsync(groups, nodes, 0, operation);

            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, ProjectionTitle.Text ?? "Groups");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not build Groups view: " + ex.Message);
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
        var scope = ContainingDirectoryScope();
        if (scope is not null) await ShowDirectoryProjectionAsync(scope.Value);
    }

    private async void PivotBranch_Click(object? sender, RoutedEventArgs e)
    {
        var scope = BranchScope();
        if (scope is not null) await ShowBranchProjectionAsync(scope.Value);
    }

    private async void PivotBestDirectoryPair_Click(object? sender, RoutedEventArgs e)
    {
        var scope = DirectoryPairSeed();
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
        var scope = BranchPairSeed();
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
        }
        catch (Exception ex)
        {
            EndProgress("Best Branch Pair search failed: " + ex.Message);
        }
    }

    private FileSystemPath? FocusedOrCurrentScope()
    {
        if (focusedNode?.SemanticPath is { } focused)
            return focused;
        return currentProjection is { IsPair: false, Primary: { } primary } ? primary : null;
    }

    private FileSystemPath? ContainingDirectoryScope()
    {
        var session = controller.Session;
        if (session is null)
            return null;

        if (!session.Selection.IsEmpty)
            return session.Selection.SelectedDirectories.CommonAncestor;

        if (focusedNode?.SemanticPath is { } focused)
            return focused;

        if (currentProjection is not { IsPair: false, Primary: { } primary } current)
            return null;

        return current.Kind == ProjectionKind.Directory
            ? ParentWithinCorpus(primary)
            : primary;
    }

    private FileSystemPath? BranchScope()
    {
        var session = controller.Session;
        if (session is null)
            return null;

        if (session.Selection.IsEmpty)
            return FocusedOrCurrentScope();

        var directories = session.Selection.SelectedDirectories;
        return directories.Single ?? directories.CommonAncestor;
    }

    private FileSystemPath? DirectoryPairSeed()
    {
        var session = controller.Session;
        if (session is null)
            return null;
        return session.Selection.IsEmpty
            ? FocusedOrCurrentScope()
            : session.Selection.SelectedDirectories.Single;
    }

    private FileSystemPath? BranchPairSeed()
    {
        var session = controller.Session;
        if (session is null)
            return null;
        if (session.Selection.IsEmpty)
            return FocusedOrCurrentScope();

        var directories = session.Selection.SelectedDirectories;
        return directories.Single ?? directories.CommonAncestor;
    }

    private FileSystemPath? ParentWithinCorpus(FileSystemPath path)
    {
        var parent = fileSystem.GetParentDirectory(path);
        var corpus = controller.Corpus;
        if (parent is null || corpus is null)
            return null;

        return corpus.Roots.Any(root =>
                fileSystem.PathsEqual(parent.Value, root.Path)
                || fileSystem.IsDescendant(parent.Value, root.Path))
            ? parent
            : null;
    }

    private bool IsCurrentProjection(ProjectionKind kind, FileSystemPath scope) =>
        currentProjection is { IsPair: false, Primary: { } primary } current
        && current.Kind == kind
        && fileSystem.PathsEqual(primary, scope);

    private async Task ShowAdHocBranchPairAsync(
        FileSystemPath first,
        FileSystemPath second,
        int sharedGroupCount)
    {
        var operation = BeginNavigation("Opening Branch Pair...", true);
        try
        {
            var leftTask = BuildBranchExplorerNodeAsync(first, operation.Token);
            var rightTask = BuildBranchExplorerNodeAsync(second, operation.Token);
            await Task.WhenAll(leftTask, rightTask);
            var left = await leftTask;
            var right = await rightTask;
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

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
            UpdatePivotCapabilities();
            CompleteNavigation(operation, $"Branch Pair — {sharedGroupCount:N0} shared Groups.");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not open Branch Pair: " + ex.Message);
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
        var operation = BeginNavigation($"Opening {target.Title}...", true);
        try
        {
            var nodeTask = BuildBranchExplorerNodeAsync(target.Path, operation.Token);
            var sharedTask = Projections.SharedGroupCountAsync(
                session,
                first,
                second,
                includeDescendants: true,
                cancellationToken: operation.Token);
            await Task.WhenAll(nodeTask, sharedTask);
            var node = await nodeTask;
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

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
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, $"Branch Pair — {shared:N0} shared Groups.");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not open Branch: " + ex.Message);
        }
    }

    private enum PairSide { Left, Right }

    private sealed record BreadcrumbTarget(
        FileSystemPath Path,
        bool IncludeDescendants,
        string Title,
        PairSide? Side);
}
