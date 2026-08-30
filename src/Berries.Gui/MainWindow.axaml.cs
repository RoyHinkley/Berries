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
        var normalized = controller.NormalizeRoots(roots.Append(path)); roots.Clear(); roots.AddRange(normalized); RefreshRoots();
        StatusText.Text = "Corpus changed; scan required.";
    }

    private void RemoveRootButton_Click(object? sender, RoutedEventArgs e)
    {
        if (RootsList.SelectedItem is not string selectedRoot) return; roots.Remove(selectedRoot); RefreshRoots();
        StatusText.Text = roots.Count == 0 ? "Select corpus roots to begin." : "Corpus changed; scan required.";
    }

    private void SelectRootsMenu_Click(object? sender, RoutedEventArgs e)
    {
        RootsPanel.IsVisible = true; ExplorerPanel.IsVisible = false;
        StatusText.Text = roots.Count == 0 ? "Select corpus roots to begin." : "Edit corpus roots, then Explore to start a new session.";
    }

    private async void ScanButton_Click(object? sender, RoutedEventArgs e)
    {
        if (roots.Count == 0) return; SetRootControlsEnabled(false); ShowExplorerWithRoots(); BeginProgress("Scanning corpus...", true);
        try
        {
            var config = BerriesConfig.Load(Path.Combine(AppContext.BaseDirectory, "Berries.config"));
            var scanProgress = new Progress<ScanProgress>(p => StatusText.Text = $"Scanning corpus — {p.FilesExamined:N0} files");
            var duplicateProgress = new Progress<DuplicateDiscoveryProgress>(p =>
            {
                StatusText.Text = $"Hashing duplicate candidates — {p.FilesHashed:N0} / {p.CandidateFiles:N0}"; StatusProgress.IsIndeterminate = false;
                StatusProgress.Value = p.CandidateFiles == 0 ? 0 : 100.0 * p.FilesHashed / p.CandidateFiles;
            });
            var scan = await controller.ScanAsync(roots, config.IsExcluded, scanProgress, duplicateProgress); ShowContentProjection();
            EndProgress($"Ready — {scan.FileCount:N0} files, with {scan.DuplicateFileCount:N0} files in {scan.DuplicateSetCount:N0} groups."
                + (scan.EvictionCount == 0 ? string.Empty : $" {scan.EvictionCount:N0} inaccessible file(s) omitted.")); UpdateCapabilities();
        }
        catch (Exception ex) { EndProgress(ex.Message); }
        finally { SetRootControlsEnabled(true); }
    }

    private void ShowExplorerWithRoots()
    {
        RootsPanel.IsVisible = false; ExplorerPanel.IsVisible = true; PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
        SetProjectionState(ProjectionKind.Corpus, []); ProjectionTitle.Text = "Corpus";
        ExplorerTree.ItemsSource = roots.Select(root => new ExplorerNode(root, semanticPath: new FileSystemPath(root))).ToArray();
    }

    private void ShowContentProjection()
    {
        var session = controller.Session; if (session is null) return;
        var groups = Projections.Groups(session); leftScope = null; rightScope = null; PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
        SetProjectionState(ProjectionKind.Groups, groups.SelectMany(set => set.Files)); ProjectionTitle.Text = "Groups";
        ExplorerTree.ItemsSource = groups.Select(set => BuildGroupNode(set.Files)).ToArray(); UpdateCapabilities();
    }

    private void SuggestCaseButton_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds; if (suggestions is null || suggestions.Count == 0) return;
        suggestionIndex = (suggestionIndex + 1) % suggestions.Count; _ = ShowBranchPairAsync(suggestions[suggestionIndex]);
    }

    private async Task ShowBranchPairAsync(BranchCounterpartSeed suggestion)
    {
        if (controller.Session is null || suggestion.Counterparts.Count == 0) return;
        var first = suggestion.Seed.Branch.Path; var second = suggestion.Counterparts[0].Branch.Path; BeginProgress("Opening Branch Pair...", true);
        try
        {
            var leftTask = BuildBranchExplorerNodeAsync(first); var rightTask = BuildBranchExplorerNodeAsync(second); await Task.WhenAll(leftTask, rightTask);
            var left = await leftTask; var right = await rightTask;
            leftScope = first; rightScope = second; PairExplorer.IsVisible = true; SingleExplorer.IsVisible = false;
            SetProjectionState(ProjectionKind.BranchPair, left.Files.Concat(right.Files), first, second);
            ProjectionTitle.Text = $"Branch Pair — {suggestion.Counterparts[0].SharedDuplicateContentCount:N0} shared groups";
            BuildPairBreadcrumbs(first, LeftScopeBreadcrumbs, true, "Branch", PairSide.Left); BuildPairBreadcrumbs(second, RightScopeBreadcrumbs, true, "Branch", PairSide.Right);
            LeftTree.ItemsSource = new[] { left }; RightTree.ItemsSource = new[] { right };
            EndProgress($"Branch Pair — {suggestion.Counterparts[0].SharedDuplicateContentCount:N0} shared Groups.");
            SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities(); UpdatePivotCapabilities();
        }
        catch (Exception ex) { EndProgress("Could not open Branch Pair: " + ex.Message); }
    }

    private void PivotContent_Click(object? sender, RoutedEventArgs e) => ShowContentProjection();
    private void PivotBranchPair_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds; if (suggestions is null || suggestionIndex < 0 || suggestionIndex >= suggestions.Count) return; _ = ShowBranchPairAsync(suggestions[suggestionIndex]);
    }

    private void InvertButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null) return; InvertTreeSelection(SingleExplorer.IsVisible ? ExplorerTree : null);
        if (PairExplorer.IsVisible) { InvertTreeSelection(LeftTree); InvertTreeSelection(RightTree); }
    }

    private void InvertTreeSelection(TreeView? tree)
    {
        if (tree?.SelectedItems is null || controller.Session is null) return;
        var selectedFiles = SelectedFiles(tree); var contents = selectedFiles.Where(file => file.Content is not null).Select(file => file.Content!.Value).ToHashSet(); if (contents.Count == 0) return;
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
        foreach (var node in items.OfType<ExplorerNode>()) { yield return node; foreach (var child in EnumerateNodes(node.Children)) yield return child; }
    }

    private async void ExcludeButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection(); if (files.Count == 0 || controller.Session is null) return;
        try { controller.Session.Exclude(files); await RefreshAfterOperationAsync($"Excluded {files.Count:N0} file(s) from the Corpus."); } catch (Exception ex) { StatusText.Text = "Exclude failed: " + ex.Message; }
    }
    private async void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection(); if (files.Count == 0 || controller.Session is null) return;
        try { controller.Session.Delete(files); await RefreshAfterOperationAsync($"Scheduled deletion of {files.Count:N0} file(s). Working Portrait updated."); } catch (Exception ex) { StatusText.Text = "Delete failed: " + ex.Message; }
    }
    private async void MoveRightButton_Click(object? sender, RoutedEventArgs e) => await MoveSelectedAsync(true);
    private async void MoveLeftButton_Click(object? sender, RoutedEventArgs e) => await MoveSelectedAsync(false);
    private async Task MoveSelectedAsync(bool leftToRight)
    {
        if (controller.Session is null || leftScope is null || rightScope is null) return;
        var source = leftToRight ? leftScope.Value : rightScope.Value; var destination = leftToRight ? rightScope.Value : leftScope.Value;
        var tree = leftToRight ? LeftTree : RightTree; var files = SelectedFiles(tree); if (files.Count == 0) return;
        try { var result = controller.Session.Move(files, source, destination); await RefreshAfterOperationAsync(MoveStatus(files.Count, result)); } catch (Exception ex) { StatusText.Text = "Move failed: " + ex.Message; }
    }
    private async void UndoButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null) return; var undone = controller.Session.Undo(); await RefreshAfterOperationAsync(undone ? "Undid the most recent operation." : "Nothing to undo.");
    }
    private static string MoveStatus(int selectedCount, MoveResult result)
    {
        var collisionCount = result.Collisions.Count; var movedCount = selectedCount - collisionCount; var text = $"Move requested for {selectedCount:N0} file(s): {movedCount:N0} moved";
        if (collisionCount > 0) text += $", {collisionCount:N0} conflict(s) skipped"; return text + ".";
    }
    private async Task RefreshAfterOperationAsync(string message)
    {
        BeginProgress("Updating Working Portrait...", true);
        try { await controller.RefreshAnalysisAsync(); ShowContentProjection(); EndProgress(message); UpdateCapabilities(); } catch (Exception ex) { EndProgress(message + " Analysis refresh failed: " + ex.Message); }
    }
    private IReadOnlyList<FileInstance> SelectedFilesFromActiveProjection()
    {
        if (PairExplorer.IsVisible) return DistinctFiles(SelectedFiles(LeftTree).Concat(SelectedFiles(RightTree))); return SelectedFiles(ExplorerTree);
    }
    private IReadOnlyList<FileInstance> SelectedFiles(TreeView tree)
    {
        if (tree.SelectedItems is null) return []; return DistinctFiles(tree.SelectedItems.OfType<ExplorerNode>().SelectMany(node => node.Files));
    }
    private IReadOnlyList<FileInstance> DistinctFiles(IEnumerable<FileInstance> files)
    {
        var result = new List<FileInstance>(); foreach (var file in files) if (!result.Any(existing => fileSystem.PathsEqual(existing.Path, file.Path))) result.Add(file); return result;
    }
    private void UpdateCapabilities()
    {
        var session = controller.Session; var hasSession = session is not null; var hasPair = currentProjection?.IsPair == true && leftScope is not null && rightScope is not null;
        MoveRightButton.IsEnabled = hasPair; MoveLeftButton.IsEnabled = hasPair; UndoButton.IsEnabled = hasSession && session!.Operations.Count > 0;
        ExecuteMenu.IsEnabled = hasSession && session!.Actions.Count > 0; var suggestions = controller.Counterparts?.Seeds;
        SuggestCaseButton.IsEnabled = suggestions is { Count: > 0 }; PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private async void ExecuteMenu_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session; if (session is null || session.Actions.Count == 0) return; var contentLosses = CountPhysicalContentLosses(session);
        var approved = await ConfirmAsync("Execute filesystem changes?", $"Planned filesystem actions: {session.Actions.Count:N0}\nGroups with no surviving physical file after the plan: {contentLosses:N0}\n\nBerries will continue past independent failures and report the results.", "Execute");
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
                    case CopyFileAction copy: EnsureParent(copy.Destination); fileSystem.CopyFile(copy.Source, copy.Destination); completed++; break;
                    case MoveFileAction move:
                        EnsureParent(move.Destination); try { fileSystem.MoveFile(move.Source, move.Destination); } catch (IOException) { fileSystem.CopyFile(move.Source, move.Destination); fileSystem.DeleteFile(move.Source); } completed++; break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (action is MoveFileAction failedMove) failedMoveDestinations.Add(failedMove.Destination); failures.Add($"{DescribeAction(action)} — {ex.Message}");
            }
        }
        EndProgress($"Execution finished — {completed:N0} completed, {skipped:N0} dependent action(s) skipped, {failures.Count:N0} failure(s)." + (failures.Count == 0 ? string.Empty : " See the failure summary."));
        if (failures.Count > 0) await ShowMessageAsync("Execution failures", string.Join(Environment.NewLine + Environment.NewLine, failures.Take(50)));
    }
    private int CountPhysicalContentLosses(BerriesSession session)
    {
        var physicallyDeleted = session.Actions.OfType<DeleteFileAction>().Select(action => action.Path).ToArray();
        return session.InitialPortrait.Files.Where(file => file.Content is not null).GroupBy(file => file.Content!.Value).Count(group => group.All(file => physicallyDeleted.Any(path => fileSystem.PathsEqual(path, file.Path))));
    }
    private void EnsureParent(FileSystemPath destination)
    {
        var parent = fileSystem.GetParentDirectory(destination); if (parent is not null && !fileSystem.Exists(parent.Value)) fileSystem.CreateDirectory(parent.Value);
    }
    private static string DescribeAction(FileAction action) => action switch
    {
        DeleteFileAction delete => $"Delete {delete.Path.Value}", CopyFileAction copy => $"Copy {copy.Source.Value} -> {copy.Destination.Value}", MoveFileAction move => $"Move {move.Source.Value} -> {move.Destination.Value}", _ => action.ToString() ?? action.GetType().Name
    };
    private async Task<bool> ConfirmAsync(string title, string message, string affirmative)
    {
        var dialog = new Window { Title = title, Width = 560, SizeToContent = SizeToContent.Height, CanResize = false, Content = BuildDialogContent(message, affirmative, out var yes, out var no) };
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false); return await dialog.ShowDialog<bool>(this);
    }
    private async Task ShowMessageAsync(string title, string message)
    {
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 12, 0, 0) }; Grid.SetRow(close, 1);
        var dialog = new Window { Title = title, Width = 700, Height = 500, Content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Avalonia.Thickness(16), Children = { new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap } }, close } } };
        close.Click += (_, _) => dialog.Close(); await dialog.ShowDialog(this);
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

public sealed class ExplorerNode(string label, IReadOnlyList<FileInstance>? files = null, FileSystemPath? semanticPath = null)
{
    public string Label { get; } = label;
    public IReadOnlyList<FileInstance> Files { get; set; } = files ?? [];
    public FileSystemPath? SemanticPath { get; } = semanticPath;
    public List<ExplorerNode> Children { get; } = [];
}
