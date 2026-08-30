using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Berries.Core;
using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.Core.Planning;
using Berries.FileSystem.Abstractions;
using Berries.FileSystem.Windows;

namespace Berries.Gui;

public partial class MainWindow : Window
{
    private readonly WindowsFileSystem fileSystem = new();
    private readonly GuiController controller;
    private readonly List<string> roots = [];
    private int suggestionIndex = -1;
    private FileSystemPath? leftScope;
    private FileSystemPath? rightScope;

    public MainWindow()
    {
        InitializeComponent();
        var engine = new BerriesEngine(fileSystem);
        controller = new GuiController(fileSystem, engine, new BranchStatisticsAnalyzer(fileSystem), new BranchCounterpartAnalyzer(fileSystem));
        RefreshRoots();
    }

    private async void AddRootButton_Click(object? sender, RoutedEventArgs e)
    {
        var directories = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select corpus root", AllowMultiple = false });
        if (directories.Count == 0) return;
        var path = directories[0].TryGetLocalPath();
        if (path is null) { StatusText.Text = "The selected directory does not have a local filesystem path."; return; }
        var normalized = controller.NormalizeRoots(roots.Append(path));
        roots.Clear(); roots.AddRange(normalized); RefreshRoots();
        StatusText.Text = "Corpus changed; scan required.";
    }

    private void RemoveRootButton_Click(object? sender, RoutedEventArgs e)
    {
        if (RootsList.SelectedItem is not string selectedRoot) return;
        roots.Remove(selectedRoot); RefreshRoots();
        StatusText.Text = roots.Count == 0 ? "Select corpus roots to begin." : "Corpus changed; scan required.";
    }

    private void SelectRootsMenu_Click(object? sender, RoutedEventArgs e)
    {
        RootsPanel.IsVisible = true; ExplorerPanel.IsVisible = false;
        StatusText.Text = roots.Count == 0 ? "Select corpus roots to begin." : "Edit corpus roots, then Explore to start a new session.";
    }

    private async void ScanButton_Click(object? sender, RoutedEventArgs e)
    {
        if (roots.Count == 0) return;
        SetRootControlsEnabled(false); ShowExplorerWithRoots(); BeginProgress("Scanning corpus...", true);
        try
        {
            var config = BerriesConfig.Load(Path.Combine(AppContext.BaseDirectory, "Berries.config"));
            var scanProgress = new Progress<ScanProgress>(p => StatusText.Text = $"Scanning corpus — {p.FilesExamined:N0} files");
            var duplicateProgress = new Progress<DuplicateDiscoveryProgress>(p =>
            {
                StatusText.Text = $"Hashing duplicate candidates — {p.FilesHashed:N0} / {p.CandidateFiles:N0}";
                StatusProgress.IsIndeterminate = false;
                StatusProgress.Value = p.CandidateFiles == 0 ? 0 : 100.0 * p.FilesHashed / p.CandidateFiles;
            });
            var scan = await controller.ScanAsync(roots, config.IsExcluded, scanProgress, duplicateProgress);
            ShowContentProjection();
            EndProgress($"Ready — {scan.FileCount:N0} files, with {scan.DuplicateFileCount:N0} files in {scan.DuplicateSetCount:N0} groups."
                + (scan.EvictionCount == 0 ? string.Empty : $" {scan.EvictionCount:N0} inaccessible file(s) omitted."));
            UpdateCapabilities();
        }
        catch (Exception ex) { EndProgress(ex.Message); }
        finally { SetRootControlsEnabled(true); }
    }

    private void ShowExplorerWithRoots()
    {
        RootsPanel.IsVisible = false; ExplorerPanel.IsVisible = true; PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
        ProjectionTitle.Text = "Corpus";
        ExplorerTree.ItemsSource = roots.Select(root => new ExplorerNode(root)).ToArray();
    }

    private void ShowContentProjection()
    {
        var session = controller.Session; if (session is null) return;
        leftScope = null; rightScope = null; PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true; ProjectionTitle.Text = "Groups";
        var nodes = session.DuplicateSets.OrderByDescending(set => set.Files.Count)
            .ThenBy(set => set.Files[0].Path.Value, StringComparer.OrdinalIgnoreCase)
            .Select(set => BuildGroupNode(set.Files)).ToArray();
        ExplorerTree.ItemsSource = nodes; UpdateCapabilities();
    }

    private void SuggestCaseButton_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds; if (suggestions is null || suggestions.Count == 0) return;
        suggestionIndex = (suggestionIndex + 1) % suggestions.Count; ShowBranchPair(suggestions[suggestionIndex]);
    }

    private void ShowBranchPair(BranchCounterpartSeed suggestion)
    {
        if (controller.Session is null || suggestion.Counterparts.Count == 0) return;
        leftScope = suggestion.Seed.Branch.Path; rightScope = suggestion.Counterparts[0].Branch.Path;
        PairExplorer.IsVisible = true; SingleExplorer.IsVisible = false;
        ProjectionTitle.Text = $"Branch Pair — {suggestion.Counterparts[0].SharedDuplicateContentCount:N0} shared groups";
        BuildPairBreadcrumbs(leftScope.Value, LeftScopeBreadcrumbs, true, "Branch", PairSide.Left);
        BuildPairBreadcrumbs(rightScope.Value, RightScopeBreadcrumbs, true, "Branch", PairSide.Right);
        LeftTree.ItemsSource = new[] { BuildBranchTree(leftScope.Value) };
        RightTree.ItemsSource = new[] { BuildBranchTree(rightScope.Value) }; UpdateCapabilities();
    }

    private ExplorerNode BuildBranchTree(FileSystemPath scope)
    {
        var session = controller.Session ?? throw new InvalidOperationException("No session.");
        var duplicateFiles = session.DuplicateSets.SelectMany(set => set.Files)
            .Where(file => fileSystem.PathsEqual(file.ParentDirectory, scope) || fileSystem.IsDescendant(file.ParentDirectory, scope)).DistinctBy(file => file.Path).ToArray();
        var root = new ExplorerNode(scope.Value);
        foreach (var file in duplicateFiles.OrderBy(file => file.Path.Value, StringComparer.OrdinalIgnoreCase))
        {
            var relative = fileSystem.GetRelativePath(scope, file.Path).Value;
            var parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var child = current.Children.FirstOrDefault(node => node.Label.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (child is null) { child = new ExplorerNode(parts[i]); current.Children.Add(child); }
                current = child;
            }
            current.Children.Add(new ExplorerNode(parts.Length == 0 ? file.Path.Value : parts[^1], [file]));
        }
        PopulateScopeFiles(root); return root;
    }

    private static IReadOnlyList<FileInstance> PopulateScopeFiles(ExplorerNode node)
    {
        if (node.Children.Count == 0) return node.Files;
        node.Files = node.Children.SelectMany(PopulateScopeFiles).Distinct().ToArray(); return node.Files;
    }

    private void PivotContent_Click(object? sender, RoutedEventArgs e) => ShowContentProjection();
    private void PivotBranchPair_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds;
        if (suggestions is null || suggestionIndex < 0 || suggestionIndex >= suggestions.Count) return; ShowBranchPair(suggestions[suggestionIndex]);
    }

    private void InvertButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null) return; InvertTreeSelection(SingleExplorer.IsVisible ? ExplorerTree : null);
        if (PairExplorer.IsVisible) { InvertTreeSelection(LeftTree); InvertTreeSelection(RightTree); }
    }

    private void InvertTreeSelection(TreeView? tree)
    {
        if (tree?.SelectedItems is null || controller.Session is null) return;
        var selectedFiles = SelectedFiles(tree);
        var contents = selectedFiles.Where(file => file.Content is not null).Select(file => file.Content!.Value).ToHashSet(); if (contents.Count == 0) return;
        var selectedPaths = selectedFiles.Select(file => file.Path).ToArray();
        var targetFiles = controller.Session.DuplicateSets.Where(set => contents.Contains(set.Content)).SelectMany(set => set.Files)
            .Where(file => !selectedPaths.Any(path => fileSystem.PathsEqual(path, file.Path))).Select(file => file.Path).ToArray();
        var leaves = EnumerateNodes(tree.ItemsSource).Where(node => node.Children.Count == 0 && node.Files.Count == 1)
            .Where(node => targetFiles.Any(path => fileSystem.PathsEqual(path, node.Files[0].Path))).Cast<object>().ToArray();
        tree.SelectedItems.Clear(); foreach (var item in leaves) tree.SelectedItems.Add(item);
    }

    private static IEnumerable<ExplorerNode> EnumerateNodes(System.Collections.IEnumerable? items)
    {
        if (items is null) yield break;
        foreach (var node in items.OfType<ExplorerNode>())
        {
            yield return node;
            foreach (var child in EnumerateNodes(node.Children)) yield return child;
        }
    }

    private async void ExcludeButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection(); if (files.Count == 0 || controller.Session is null) return;
        controller.Session.Exclude(files); await AfterPortraitOperationAsync($"Excluded {files.Count:N0} file(s) from the Corpus.");
    }
    private async void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection(); if (files.Count == 0 || controller.Session is null) return;
        controller.Session.Delete(files); await AfterPortraitOperationAsync($"Scheduled deletion of {files.Count:N0} file(s).");
    }
    private async void MoveRightButton_Click(object? sender, RoutedEventArgs e)
    {
        if (leftScope is null || rightScope is null || controller.Session is null) return; var files = SelectedFiles(LeftTree); if (files.Count == 0) return;
        var result = controller.Session.Move(files, leftScope.Value, rightScope.Value); await AfterPortraitOperationAsync(MoveStatus(files.Count, result));
    }
    private async void MoveLeftButton_Click(object? sender, RoutedEventArgs e)
    {
        if (leftScope is null || rightScope is null || controller.Session is null) return; var files = SelectedFiles(RightTree); if (files.Count == 0) return;
        var result = controller.Session.Move(files, rightScope.Value, leftScope.Value); await AfterPortraitOperationAsync(MoveStatus(files.Count, result));
    }
    private static string MoveStatus(int requested, MoveResult result) => result.Collisions.Count == 0
        ? $"Move resolved {requested:N0} selected file(s)." : $"Move completed where possible; {result.Collisions.Count:N0} destination collision(s) with different content were left unchanged.";

    private async void UndoButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session?.Undo() != true) return; await AfterPortraitOperationAsync("Undid the most recent portrait operation.");
    }
    private async Task AfterPortraitOperationAsync(string message)
    {
        RefreshCurrentProjection(); UpdateCapabilities(); BeginProgress(message + " Updating analysis...", true);
        try { await controller.RefreshAnalysisAsync(); EndProgress(message); UpdateCapabilities(); }
        catch (Exception ex) { EndProgress(message + " Analysis update failed: " + ex.Message); }
    }
    private void RefreshCurrentProjection()
    {
        if (PairExplorer.IsVisible && leftScope is not null && rightScope is not null)
        { LeftTree.ItemsSource = new[] { BuildBranchTree(leftScope.Value) }; RightTree.ItemsSource = new[] { BuildBranchTree(rightScope.Value) }; }
        else ShowContentProjection();
    }
    private IReadOnlyList<FileInstance> SelectedFilesFromActiveProjection() => PairExplorer.IsVisible
        ? DistinctFiles(SelectedFiles(LeftTree).Concat(SelectedFiles(RightTree))) : SelectedFiles(ExplorerTree);
    private IReadOnlyList<FileInstance> SelectedFiles(TreeView tree)
    {
        if (tree.SelectedItems is null) return [];
        return DistinctFiles(tree.SelectedItems.OfType<ExplorerNode>().SelectMany(node => node.Files));
    }
    private IReadOnlyList<FileInstance> DistinctFiles(IEnumerable<FileInstance> files)
    {
        var result = new List<FileInstance>(); foreach (var file in files)
            if (!result.Any(existing => fileSystem.PathsEqual(existing.Path, file.Path))) result.Add(file); return result;
    }
    private void UpdateCapabilities()
    {
        var session = controller.Session; var hasSession = session is not null; var hasPair = PairExplorer.IsVisible && leftScope is not null && rightScope is not null;
        MoveRightButton.IsEnabled = hasPair; MoveLeftButton.IsEnabled = hasPair; UndoButton.IsEnabled = hasSession && session!.Operations.Count > 0;
        ExecuteMenu.IsEnabled = hasSession && session!.Actions.Count > 0; var suggestions = controller.Counterparts?.Seeds;
        SuggestCaseButton.IsEnabled = suggestions is { Count: > 0 }; PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private async void ExecuteMenu_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session; if (session is null || session.Actions.Count == 0) return;
        var contentLosses = CountPhysicalContentLosses(session);
        var approved = await ConfirmAsync("Execute filesystem changes?",
            $"Planned filesystem actions: {session.Actions.Count:N0}\nGroups with no surviving physical file after the plan: {contentLosses:N0}\n\nBerries will continue past independent failures and report the results.", "Execute");
        if (!approved) return;
        BeginProgress("Executing filesystem actions...", true); var completed = 0; var skipped = 0; var failures = new List<string>(); var failedMoveDestinations = new List<FileSystemPath>();
        foreach (var action in session.Actions)
        {
            try
            {
                switch (action)
                {
                    case DeleteFileAction delete:
                        if (failedMoveDestinations.Any(path => fileSystem.PathsEqual(path, delete.Path))) { skipped++; continue; }
                        fileSystem.DeleteFile(delete.Path); completed++; break;
                    case CopyFileAction copy:
                        EnsureParent(copy.Destination); fileSystem.CopyFile(copy.Source, copy.Destination); completed++; break;
                    case MoveFileAction move:
                        EnsureParent(move.Destination);
                        try { fileSystem.MoveFile(move.Source, move.Destination); }
                        catch (IOException) { fileSystem.CopyFile(move.Source, move.Destination); fileSystem.DeleteFile(move.Source); }
                        completed++; break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (action is MoveFileAction failedMove) failedMoveDestinations.Add(failedMove.Destination);
                failures.Add($"{DescribeAction(action)} — {ex.Message}");
            }
        }
        EndProgress($"Execution finished — {completed:N0} completed, {skipped:N0} dependent action(s) skipped, {failures.Count:N0} failure(s)." + (failures.Count == 0 ? string.Empty : " See the failure summary."));
        if (failures.Count > 0) await ShowMessageAsync("Execution failures", string.Join(Environment.NewLine + Environment.NewLine, failures.Take(50)));
    }

    private int CountPhysicalContentLosses(BerriesSession session)
    {
        var physicallyDeleted = session.Actions.OfType<DeleteFileAction>().Select(action => action.Path).ToArray();
        return session.InitialPortrait.Files.Where(file => file.Content is not null).GroupBy(file => file.Content!.Value)
            .Count(group => group.All(file => physicallyDeleted.Any(path => fileSystem.PathsEqual(path, file.Path))));
    }
    private void EnsureParent(FileSystemPath destination)
    {
        var parent = fileSystem.GetParentDirectory(destination); if (parent is not null && !fileSystem.Exists(parent.Value)) fileSystem.CreateDirectory(parent.Value);
    }
    private static string DescribeAction(FileAction action) => action switch
    {
        DeleteFileAction delete => $"Delete {delete.Path.Value}", CopyFileAction copy => $"Copy {copy.Source.Value} -> {copy.Destination.Value}",
        MoveFileAction move => $"Move {move.Source.Value} -> {move.Destination.Value}", _ => action.ToString() ?? action.GetType().Name
    };
    private async Task<bool> ConfirmAsync(string title, string message, string affirmative)
    {
        var dialog = new Window { Title = title, Width = 560, SizeToContent = SizeToContent.Height, CanResize = false, Content = BuildDialogContent(message, affirmative, out var yes, out var no) };
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false); return await dialog.ShowDialog<bool>(this);
    }
    private async Task ShowMessageAsync(string title, string message)
    {
        var close = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(close, 1);

        var dialog = new Window
        {
            Title = title,
            Width = 700,
            Height = 500,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Margin = new Avalonia.Thickness(16),
                Children =
                {
                    new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = message,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        }
                    },
                    close
                }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
    private static Control BuildDialogContent(string message, string affirmative, out Button yes, out Button no)
    {
        yes = new Button { Content = affirmative }; no = new Button { Content = "Cancel" }; var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(no); buttons.Children.Add(yes); var panel = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }); panel.Children.Add(buttons); return panel;
    }
    private void SetRootControlsEnabled(bool enabled) { AddRootButton.IsEnabled = enabled; RemoveRootButton.IsEnabled = enabled; ScanButton.IsEnabled = enabled; }
    private void BeginProgress(string text, bool indeterminate) { StatusText.Text = text; StatusProgress.IsVisible = true; StatusProgress.IsIndeterminate = indeterminate; if (!indeterminate) StatusProgress.Value = 0; }
    private void EndProgress(string text) { StatusText.Text = text; StatusProgress.IsVisible = false; StatusProgress.IsIndeterminate = false; }
    private void RefreshRoots() { RootsList.ItemsSource = null; RootsList.ItemsSource = roots.ToArray(); }
    private void ExitMenu_Click(object? sender, RoutedEventArgs e) => Close();
}

public sealed class ExplorerNode(string label, IReadOnlyList<FileInstance>? files = null)
{
    public string Label { get; } = label;
    public IReadOnlyList<FileInstance> Files { get; set; } = files ?? [];
    public List<ExplorerNode> Children { get; } = [];
}