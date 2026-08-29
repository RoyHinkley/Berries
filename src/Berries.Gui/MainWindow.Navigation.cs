using Avalonia.Controls;
using Avalonia.Controls.Selection;
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
            RootsPanel.IsVisible = false;
            ExplorerPanel.IsVisible = true;
            StatusText.Text = "Returned to the current session.";
            return;
        }
        ScanButton_Click(sender, e);
    }

    private bool CorpusRootsMatchCurrentSelection()
    {
        var corpus = controller.Corpus;
        if (corpus is null || corpus.Roots.Count != roots.Count) return false;
        return roots.All(root => corpus.Roots.Any(existing =>
            fileSystem.PathsEqual(existing.Path, new FileSystemPath(root))));
    }

    private void ExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = sender is TreeView tree && tree.SelectedItems is { Count: > 0 };
        InvertButton.IsEnabled = hasSelection;
        ExcludeButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void UpdatePivotCapabilities()
    {
        var scope = SelectedScope();
        PivotContentMenu.IsEnabled = controller.Session is not null;
        PivotDirectoryMenu.IsEnabled = scope is not null;
        PivotBranchMenu.IsEnabled = scope is not null;
        PivotBestDirectoryPairMenu.IsEnabled = scope is not null && FindBestDirectoryPair(scope.Value) is not null;
        PivotBestBranchPairMenu.IsEnabled = scope is not null && HasBranchPairCandidate(scope.Value);
        var suggestions = controller.Counterparts?.Seeds;
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private bool HasBranchPairCandidate(FileSystemPath scope)
    {
        var branches = controller.BranchStatistics?.Branches;
        if (branches is null) return false;
        return branches.Any(branch => fileSystem.PathsEqual(branch.Path, scope) && branch.DuplicateContentCount > 0)
            && branches.Any(branch => !fileSystem.PathsEqual(branch.Path, scope)
                && !fileSystem.IsDescendant(branch.Path, scope)
                && !fileSystem.IsDescendant(scope, branch.Path)
                && branch.DuplicateContentCount > 0);
    }

    private void UpdateSelectionStatus()
    {
        if (StatusProgress.IsVisible) return;
        var nodes = SelectedNodesFromActiveProjection();
        if (nodes.Count == 0) return;
        var files = DistinctFiles(nodes.SelectMany(node => node.Files));
        var groups = files.Where(file => file.Content is not null).Select(file => file.Content!.Value).Distinct().Count();
        if (files.Count == 1)
            StatusText.Text = $"{files[0].Path.Value} — {files[0].Length:N0} bytes";
        else if (files.Count > 0)
            StatusText.Text = $"Selection — {files.Count:N0} files, {groups:N0} Groups";
        else
            StatusText.Text = nodes.Count == 1 ? nodes[0].Label : $"Selection — {nodes.Count:N0} items";
    }

    private IReadOnlyList<ExplorerNode> SelectedNodesFromActiveProjection()
    {
        IEnumerable<object> selected = PairExplorer.IsVisible
            ? SelectedObjects(LeftTree).Concat(SelectedObjects(RightTree))
            : SelectedObjects(ExplorerTree);
        return selected.OfType<ExplorerNode>().Distinct().ToArray();
    }

    private static IEnumerable<object> SelectedObjects(TreeView tree) =>
        tree.SelectedItems?.Cast<object>() ?? [];

    private void PivotSelectedContent_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session;
        if (session is null) return;
        var selected = DistinctFiles(SelectedNodesFromActiveProjection().SelectMany(node => node.Files));
        var contentIds = selected.Where(file => file.Content is not null)
            .Select(file => file.Content!.Value).Distinct().ToHashSet();
        if (contentIds.Count == 0) { ShowContentProjection(); return; }

        currentScope = null; leftScope = null; rightScope = null;
        PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
        BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear();
        ProjectionTitle.Text = contentIds.Count == 1 ? "Group" : $"Groups — {contentIds.Count:N0} selected";
        ExplorerTree.ItemsSource = session.DuplicateSets
            .Where(set => contentIds.Contains(set.Content))
            .OrderByDescending(set => set.Files.Count)
            .Select(set => BuildGroupNode(set.Files)).ToArray();
        UpdateCapabilities();
    }

    private ExplorerNode BuildGroupNode(IReadOnlyList<FileInstance> files)
    {
        var names = files.Select(file => Path.GetFileName(file.Path.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var shownNames = string.Join(", ", names.Take(2));
        if (names.Length > 2) shownNames += ", …";
        var node = new ExplorerNode($"{shownNames} — {files.Count:N0} files", files);
        foreach (var file in files.OrderBy(file => file.Path.Value, StringComparer.OrdinalIgnoreCase))
            node.Children.Add(new ExplorerNode(file.Path.Value, [file]));
        return node;
    }

    private void PivotDirectory_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope(); if (scope is null) return;
        ShowScopeProjection(scope.Value, false, "Directory");
    }

    private void PivotBranch_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope(); if (scope is null) return;
        ShowScopeProjection(scope.Value, true, "Branch");
    }

    private void PivotBestDirectoryPair_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope(); if (scope is null) return;
        var pair = FindBestDirectoryPair(scope.Value); if (pair is null) return;
        ShowDirectoryPair(pair);
    }

    private void PivotBestBranchPair_Click(object? sender, RoutedEventArgs e)
    {
        var scope = SelectedScope(); if (scope is null) return;
        var pair = FindBestBranchPair(scope.Value); if (pair is null) return;
        ShowAdHocBranchPair(pair.Value.First, pair.Value.Second, pair.Value.SharedContentCount);
    }

    private FileSystemPath? SelectedScope()
    {
        var nodes = SelectedNodesFromActiveProjection();
        if (nodes.Count == 1) return InferSelectedScope(nodes[0]);
        return nodes.Count == 0 ? currentScope : null;
    }

    private FileSystemPath? InferSelectedScope(ExplorerNode node)
    {
        var files = DistinctFiles(node.Files);
        if (files.Count == 0) return null;
        if (files.Count == 1) return files[0].ParentDirectory;
        FileSystemPath? candidate = files[0].ParentDirectory;
        while (candidate is not null)
        {
            var path = candidate.Value;
            var labelMatches = string.Equals(path.Value, node.Label, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path.Value), node.Label, StringComparison.OrdinalIgnoreCase);
            var containsAll = files.All(file => fileSystem.PathsEqual(file.ParentDirectory, path)
                || fileSystem.IsDescendant(file.ParentDirectory, path));
            if (labelMatches && containsAll) return path;
            candidate = fileSystem.GetParentDirectory(path);
        }
        return null;
    }

    private DirectoryPair? FindBestDirectoryPair(FileSystemPath scope) =>
        controller.DirectoryAnalysis?.DirectoryPairs
            .Where(pair => fileSystem.PathsEqual(pair.First, scope) || fileSystem.PathsEqual(pair.Second, scope))
            .OrderByDescending(pair => pair.SharedContentCount).FirstOrDefault();

    private (FileSystemPath First, FileSystemPath Second, int SharedContentCount)? FindBestBranchPair(FileSystemPath scope)
    {
        var session = controller.Session;
        var branches = controller.BranchStatistics?.Branches;
        if (session is null || branches is null) return null;
        var seed = branches.FirstOrDefault(branch => fileSystem.PathsEqual(branch.Path, scope));
        if (seed is null || seed.DuplicateContentCount == 0) return null;
        var seedContents = ContentsUnder(scope);
        if (seedContents.Count == 0) return null;
        (FileSystemPath First, FileSystemPath Second, int SharedContentCount)? best = null;
        double bestScore = 0;
        foreach (var candidate in branches)
        {
            if (fileSystem.PathsEqual(candidate.Path, scope)
                || fileSystem.IsDescendant(candidate.Path, scope)
                || fileSystem.IsDescendant(scope, candidate.Path)
                || candidate.DuplicateContentCount == 0) continue;
            var candidateContents = ContentsUnder(candidate.Path);
            var shared = seedContents.Count(content => candidateContents.Contains(content));
            if (shared == 0) continue;
            var union = seedContents.Count + candidateContents.Count - shared;
            var score = union == 0 ? 0 : shared * ((double)shared / union);
            if (best is null || score > bestScore) { bestScore = score; best = (scope, candidate.Path, shared); }
        }
        return best;
    }

    private HashSet<ContentId> ContentsUnder(FileSystemPath scope)
    {
        var session = controller.Session ?? throw new InvalidOperationException("No session.");
        return session.DuplicateSets
            .Where(set => set.Files.Any(file => fileSystem.PathsEqual(file.ParentDirectory, scope)
                || fileSystem.IsDescendant(file.ParentDirectory, scope)))
            .Select(set => set.Content).ToHashSet();
    }

    private void ShowDirectoryPair(DirectoryPair pair)
    {
        currentScope = null; leftScope = pair.First; rightScope = pair.Second;
        PairExplorer.IsVisible = true; SingleExplorer.IsVisible = false;
        BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear();
        ProjectionTitle.Text = $"Directory Pair — {pair.SharedContentCount:N0} shared Groups";
        LeftScopeText.Text = CorpusRelativeDisplay(pair.First); RightScopeText.Text = CorpusRelativeDisplay(pair.Second);
        LeftTree.ItemsSource = new[] { BuildDirectoryTree(pair.First) };
        RightTree.ItemsSource = new[] { BuildDirectoryTree(pair.Second) };
        UpdateCapabilities();
    }

    private ExplorerNode BuildDirectoryTree(FileSystemPath scope)
    {
        var session = controller.Session ?? throw new InvalidOperationException("No session.");
        var files = session.DuplicateSets.SelectMany(set => set.Files)
            .Where(file => fileSystem.PathsEqual(file.ParentDirectory, scope))
            .OrderBy(file => file.Path.Value, StringComparer.OrdinalIgnoreCase).ToArray();
        var root = new ExplorerNode(scope.Value, files);
        foreach (var file in files) root.Children.Add(new ExplorerNode(Path.GetFileName(file.Path.Value), [file]));
        return root;
    }

    private void ShowAdHocBranchPair(FileSystemPath first, FileSystemPath second, int sharedContentCount)
    {
        currentScope = null; leftScope = first; rightScope = second;
        PairExplorer.IsVisible = true; SingleExplorer.IsVisible = false;
        BreadcrumbPanel.IsVisible = false; BreadcrumbPanel.Children.Clear();
        ProjectionTitle.Text = $"Branch Pair — {sharedContentCount:N0} shared Groups";
        LeftScopeText.Text = CorpusRelativeDisplay(first); RightScopeText.Text = CorpusRelativeDisplay(second);
        LeftTree.ItemsSource = new[] { BuildBranchTree(first) };
        RightTree.ItemsSource = new[] { BuildBranchTree(second) };
        UpdateCapabilities();
    }

    private void ShowScopeProjection(FileSystemPath scope, bool includeDescendants, string title)
    {
        if (controller.Session is null) return;
        currentScope = scope; scopeIncludesDescendants = includeDescendants; scopeProjectionTitle = title;
        leftScope = null; rightScope = null; PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
        ProjectionTitle.Text = title; BuildBreadcrumbs(scope);
        ExplorerTree.ItemsSource = includeDescendants
            ? new[] { BuildBranchTree(scope) }
            : new[] { BuildDirectoryTree(scope) };
        UpdateCapabilities(); UpdatePivotCapabilities();
    }

    private void BuildBreadcrumbs(FileSystemPath scope)
    {
        BreadcrumbPanel.Children.Clear();
        var root = CorpusRootFor(scope);
        if (root is null) { BreadcrumbPanel.IsVisible = false; return; }
        var chain = new List<FileSystemPath>(); var current = scope;
        while (true)
        {
            chain.Add(current);
            if (fileSystem.PathsEqual(current, root.Value)) break;
            var parent = fileSystem.GetParentDirectory(current); if (parent is null) break;
            current = parent.Value;
        }
        chain.Reverse();
        for (var i = 0; i < chain.Count; i++)
        {
            if (i > 0) BreadcrumbPanel.Children.Add(new TextBlock { Text = "›", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var path = chain[i];
            var button = new Button { Content = i == 0 ? path.Value : Path.GetFileName(path.Value), Padding = new Avalonia.Thickness(4, 1), Tag = path };
            button.Click += Breadcrumb_Click; BreadcrumbPanel.Children.Add(button);
        }
        BreadcrumbPanel.IsVisible = true;
    }

    private void Breadcrumb_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FileSystemPath path }) ShowScopeProjection(path, scopeIncludesDescendants, scopeProjectionTitle);
    }

    private FileSystemPath? CorpusRootFor(FileSystemPath path)
    {
        var corpus = controller.Corpus; if (corpus is null) return null;
        foreach (var root in corpus.Roots.Select(item => item.Path))
            if (fileSystem.PathsEqual(path, root) || fileSystem.IsDescendant(path, root)) return root;
        return null;
    }

    private string CorpusRelativeDisplay(FileSystemPath path)
    {
        var root = CorpusRootFor(path); if (root is null) return path.Value;
        if (fileSystem.PathsEqual(root.Value, path)) return root.Value.Value;
        return root.Value.Value + " › " + fileSystem.GetRelativePath(root.Value, path).Value
            .Replace(Path.DirectorySeparatorChar.ToString(), " › ")
            .Replace(Path.AltDirectorySeparatorChar.ToString(), " › ");
    }
}
