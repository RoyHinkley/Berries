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
        controller = new GuiController(
            fileSystem,
            engine,
            new BranchStatisticsAnalyzer(fileSystem),
            new BranchCounterpartAnalyzer(fileSystem));
        RefreshRoots();
    }

    private async void AddRootButton_Click(object? sender, RoutedEventArgs e)
    {
        var directories = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select corpus root",
            AllowMultiple = false
        });
        if (directories.Count == 0) return;

        var path = directories[0].TryGetLocalPath();
        if (path is null)
        {
            StatusText.Text = "The selected directory does not have a local filesystem path.";
            return;
        }

        var normalized = controller.NormalizeRoots(roots.Append(path));
        roots.Clear();
        roots.AddRange(normalized);
        RefreshRoots();
        StatusText.Text = "Corpus changed; scan required.";
    }

    private void RemoveRootButton_Click(object? sender, RoutedEventArgs e)
    {
        if (RootsList.SelectedItem is not string selectedRoot) return;
        roots.Remove(selectedRoot);
        RefreshRoots();
        StatusText.Text = roots.Count == 0 ? "Select corpus roots to begin." : "Corpus changed; scan required.";
    }

    private void SelectRootsMenu_Click(object? sender, RoutedEventArgs e)
    {
        RootsPanel.IsVisible = true;
        ExplorerPanel.IsVisible = false;
        StatusText.Text = roots.Count == 0 ? "Select corpus roots to begin." : "Edit corpus roots, then Scan to start a new session.";
    }

    private async void ScanButton_Click(object? sender, RoutedEventArgs e)
    {
        if (roots.Count == 0) return;

        SetRootControlsEnabled(false);
        ShowExplorerWithRoots();
        BeginProgress("Scanning corpus...", indeterminate: true);

        try
        {
            var config = BerriesConfig.Load(Path.Combine(AppContext.BaseDirectory, "Berries.config"));
            var scanProgress = new Progress<ScanProgress>(progress =>
            {
                StatusText.Text = $"Scanning corpus — {progress.FilesExamined:N0} files";
            });
            var duplicateProgress = new Progress<DuplicateDiscoveryProgress>(progress =>
            {
                StatusText.Text = $"Hashing duplicate candidates — {progress.FilesHashed:N0} / {progress.CandidateFiles:N0}";
                StatusProgress.IsIndeterminate = false;
                StatusProgress.Value = progress.CandidateFiles == 0
                    ? 0
                    : 100.0 * progress.FilesHashed / progress.CandidateFiles;
            });

            var scan = await controller.ScanAsync(
                roots,
                config.IsExcluded,
                scanProgress,
                duplicateProgress);

            ShowContentProjection();
            EndProgress($"Ready — {scan.FileCount:N0} files, {scan.DuplicateSetCount:N0} duplicate Contents, {scan.DuplicateFileCount:N0} duplicate instances."
                + (scan.EvictionCount == 0 ? string.Empty : $" {scan.EvictionCount:N0} inaccessible file(s) omitted."));
            UpdateCapabilities();
        }
        catch (Exception ex)
        {
            EndProgress(ex.Message);
        }
        finally
        {
            SetRootControlsEnabled(true);
        }
    }

    private void ShowExplorerWithRoots()
    {
        RootsPanel.IsVisible = false;
        ExplorerPanel.IsVisible = true;
        PairExplorer.IsVisible = false;
        SingleExplorer.IsVisible = true;
        ProjectionTitle.Text = "Corpus";
        ExplorerTree.ItemsSource = roots.Select(root => CreateTreeItem(new ExplorerNode(root))).ToArray();
    }

    private void ShowContentProjection()
    {
        var session = controller.Session;
        if (session is null) return;

        leftScope = null;
        rightScope = null;
        PairExplorer.IsVisible = false;
        SingleExplorer.IsVisible = true;
        ProjectionTitle.Text = "Content";

        var nodes = session.DuplicateSets
            .OrderByDescending(set => set.Files.Count)
            .ThenBy(set => set.Files[0].Path.Value, StringComparer.OrdinalIgnoreCase)
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

    private void SuggestCaseButton_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds;
        if (suggestions is null || suggestions.Count == 0) return;

        suggestionIndex = (suggestionIndex + 1) % suggestions.Count;
        ShowBranchPair(suggestions[suggestionIndex]);
    }

    private void ShowBranchPair(BranchCounterpartSeed suggestion)
    {
        if (controller.Session is null || suggestion.Counterparts.Count == 0) return;

        leftScope = suggestion.Seed.Branch.Path;
        rightScope = suggestion.Counterparts[0].Branch.Path;
        PairExplorer.IsVisible = true;
        SingleExplorer.IsVisible = false;
        ProjectionTitle.Text = $"Branch Pair — {suggestion.Counterparts[0].SharedDuplicateContentCount:N0} shared Contents";
        LeftScopeText.Text = leftScope.Value.Value;
        RightScopeText.Text = rightScope.Value.Value;

        LeftTree.ItemsSource = [CreateTreeItem(BuildBranchTree(leftScope.Value))];
        RightTree.ItemsSource = [CreateTreeItem(BuildBranchTree(rightScope.Value))];
        UpdateCapabilities();
    }

    private ExplorerNode BuildBranchTree(FileSystemPath scope)
    {
        var session = controller.Session ?? throw new InvalidOperationException("No session.");
        var duplicateFiles = session.DuplicateSets.SelectMany(set => set.Files)
            .Where(file => fileSystem.PathsEqual(file.ParentDirectory, scope) || fileSystem.IsDescendant(file.ParentDirectory, scope))
            .DistinctBy(file => file.Path)
            .ToArray();

        var root = new ExplorerNode(scope.Value);
        foreach (var file in duplicateFiles.OrderBy(file => file.Path.Value, StringComparer.OrdinalIgnoreCase))
        {
            var relative = fileSystem.GetRelativePath(scope, file.Path).Value;
            var parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var child = current.Children.FirstOrDefault(node => node.Label.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (child is null)
                {
                    child = new ExplorerNode(parts[i]);
                    current.Children.Add(child);
                }
                current = child;
            }
            current.Children.Add(new ExplorerNode(parts.Length == 0 ? file.Path.Value : parts[^1], [file]));
        }

        PopulateScopeFiles(root);
        return root;
    }

    private static IReadOnlyList<FileInstance> PopulateScopeFiles(ExplorerNode node)
    {
        if (node.Children.Count == 0)
            return node.Files;
        node.Files = node.Children.SelectMany(PopulateScopeFiles).Distinct().ToArray();
        return node.Files;
    }

    private static TreeViewItem CreateTreeItem(ExplorerNode node)
    {
        var item = new TreeViewItem { Header = node.Label, Tag = node };
        if (node.Children.Count > 0)
            item.ItemsSource = node.Children.Select(CreateTreeItem).ToArray();
        return item;
    }

    private void PivotContent_Click(object? sender, RoutedEventArgs e) => ShowContentProjection();

    private void PivotBranchPair_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds;
        if (suggestions is null || suggestionIndex < 0 || suggestionIndex >= suggestions.Count) return;
        ShowBranchPair(suggestions[suggestionIndex]);
    }

    private void InvertButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null) return;
        InvertTreeSelection(SingleExplorer.IsVisible ? ExplorerTree : null);
        if (PairExplorer.IsVisible)
        {
            InvertTreeSelection(LeftTree);
            InvertTreeSelection(RightTree);
        }
    }

    private void InvertTreeSelection(TreeView? tree)
    {
        if (tree?.SelectedItems is null || controller.Session is null) return;
        var selectedFiles = SelectedFiles(tree);
        var contents = selectedFiles.Where(file => file.Content is not null).Select(file => file.Content!.Value).ToHashSet();
        if (contents.Count == 0) return;

        var selectedPaths = selectedFiles.Select(file => file.Path).ToArray();
        var targetFiles = controller.Session.DuplicateSets
            .Where(set => contents.Contains(set.Content))
            .SelectMany(set => set.Files)
            .Where(file => !selectedPaths.Any(path => fileSystem.PathsEqual(path, file.Path)))
            .Select(file => file.Path)
            .ToArray();

        var leaves = EnumerateItems(tree.ItemsSource)
            .Where(item => item.Tag is ExplorerNode node && node.Children.Count == 0 && node.Files.Count == 1)
            .Where(item => targetFiles.Any(path => fileSystem.PathsEqual(path, ((ExplorerNode)item.Tag!).Files[0].Path)))
            .Cast<object>()
            .ToArray();

        tree.SelectedItems.Clear();
        foreach (var item in leaves)
            tree.SelectedItems.Add(item);
    }

    private static IEnumerable<TreeViewItem> EnumerateItems(System.Collections.IEnumerable? items)
    {
        if (items is null) yield break;
        foreach (var item in items.OfType<TreeViewItem>())
        {
            yield return item;
            foreach (var child in EnumerateItems(item.ItemsSource))
                yield return child;
        }
    }

    private async void ExcludeButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection();
        if (files.Count == 0 || controller.Session is null) return;
        controller.Session.Exclude(files);
        await AfterPortraitOperationAsync($"Excluded {files.Count:N0} duplicate instance(s) from the Corpus.");
    }

    private async void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection();
        if (files.Count == 0 || controller.Session is null) return;
        controller.Session.Delete(files);
        await AfterPortraitOperationAsync($"Scheduled deletion of {files.Count:N0} duplicate instance(s).");
    }

    private async void MoveRightButton_Click(object? sender, RoutedEventArgs e)
    {
        if (leftScope is null || rightScope is null || controller.Session is null) return;
        var files = SelectedFiles(LeftTree);
        if (files.Count == 0) return;
        var result = controller.Session.Move(files, leftScope.Value, rightScope.Value);
        await AfterPortraitOperationAsync(MoveStatus(files.Count, result));
    }

    private async void MoveLeftButton_Click(object? sender, RoutedEventArgs e)
    {
        if (leftScope is null || rightScope is null || controller.Session is null) return;
        var files = SelectedFiles(RightTree);
        if (files.Count == 0) return;
        var result = controller.Session.Move(files, rightScope.Value, leftScope.Value);
        await AfterPortraitOperationAsync(MoveStatus(files.Count, result));
    }

    private static string MoveStatus(int requested, MoveResult result) =>
        result.Collisions.Count == 0
            ? $"Move resolved {requested:N0} selected duplicate instance(s)."
            : $"Move completed where possible; {result.Collisions.Count:N0} different-Content destination collision(s) were left unchanged.";

    private async void UndoButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session?.Undo() != true) return;
        await AfterPortraitOperationAsync("Undid the most recent portrait operation.");
    }

    private async Task AfterPortraitOperationAsync(string message)
    {
        RefreshCurrentProjection();
        UpdateCapabilities();
        BeginProgress(message + " Updating analysis...", indeterminate: true);
        try
        {
            await controller.RefreshAnalysisAsync();
            EndProgress(message);
            UpdateCapabilities();
        }
        catch (Exception ex)
        {
            EndProgress(message + " Analysis update failed: " + ex.Message);
        }
    }

    private void RefreshCurrentProjection()
    {
        if (PairExplorer.IsVisible && leftScope is not null && rightScope is not null)
        {
            LeftTree.ItemsSource = [CreateTreeItem(BuildBranchTree(leftScope.Value))];
            RightTree.ItemsSource = [CreateTreeItem(BuildBranchTree(rightScope.Value))];
        }
        else
        {
            ShowContentProjection();
        }
    }

    private IReadOnlyList<FileInstance> SelectedFilesFromActiveProjection() =>
        PairExplorer.IsVisible
            ? DistinctFiles(SelectedFiles(LeftTree).Concat(SelectedFiles(RightTree)))
            : SelectedFiles(ExplorerTree);

    private IReadOnlyList<FileInstance> SelectedFiles(TreeView tree)
    {
        if (tree.SelectedItems is null) return [];
        return DistinctFiles(tree.SelectedItems
            .OfType<TreeViewItem>()
            .Select(item => item.Tag)
            .OfType<ExplorerNode>()
            .SelectMany(node => node.Files));
    }

    private IReadOnlyList<FileInstance> DistinctFiles(IEnumerable<FileInstance> files)
    {
        var result = new List<FileInstance>();
        foreach (var file in files)
            if (!result.Any(existing => fileSystem.PathsEqual(existing.Path, file.Path)))
                result.Add(file);
        return result;
    }

    private void UpdateCapabilities()
    {
        var session = controller.Session;
        var hasSession = session is not null;
        var hasPair = PairExplorer.IsVisible && leftScope is not null && rightScope is not null;
        MoveRightButton.IsEnabled = hasPair;
        MoveLeftButton.IsEnabled = hasPair;
        UndoButton.IsEnabled = hasSession && session!.Operations.Count > 0;
        ExecuteMenu.IsEnabled = hasSession && session!.Actions.Count > 0;
        var suggestions = controller.Counterparts?.Seeds;
        SuggestCaseButton.IsEnabled = suggestions is { Count: > 0 };
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private async void ExecuteMenu_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session;
        if (session is null || session.Actions.Count == 0) return;

        var contentLosses = CountPhysicalContentLosses(session);
        var approved = await ConfirmAsync(
            "Execute filesystem changes?",
            $"Planned filesystem actions: {session.Actions.Count:N0}\nContents with no surviving physical instance after the plan: {contentLosses:N0}\n\nBerries will continue past independent failures and report the results.",
            "Execute");
        if (!approved) return;

        BeginProgress("Executing filesystem actions...", indeterminate: true);
        var completed = 0;
        var skipped = 0;
        var failures = new List<string>();
        var failedMoveDestinations = new List<FileSystemPath>();

        foreach (var action in session.Actions)
        {
            try
            {
                switch (action)
                {
                    case DeleteFileAction delete:
                        if (failedMoveDestinations.Any(path => fileSystem.PathsEqual(path, delete.Path)))
                        {
                            skipped++;
                            continue;
                        }
                        fileSystem.DeleteFile(delete.Path);
                        completed++;
                        break;

                    case CopyFileAction copy:
                        EnsureParent(copy.Destination);
                        fileSystem.CopyFile(copy.Source, copy.Destination);
                        completed++;
                        break;

                    case MoveFileAction move:
                        EnsureParent(move.Destination);
                        try
                        {
                            fileSystem.MoveFile(move.Source, move.Destination);
                            completed++;
                        }
                        catch (IOException)
                        {
                            try
                            {
                                fileSystem.CopyFile(move.Source, move.Destination);
                                fileSystem.DeleteFile(move.Source); // dependent deletion only after successful copy
                                completed++;
                            }
                            catch (Exception fallbackFailure)
                            {
                                failedMoveDestinations.Add(move.Destination);
                                failures.Add($"Move {move.Source} -> {move.Destination}: {fallbackFailure.Message}");
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                if (action is MoveFileAction failedMove)
                    failedMoveDestinations.Add(failedMove.Destination);
                failures.Add($"{action}: {ex.Message}");
            }
        }

        EndProgress($"Execution complete — {completed:N0} completed, {skipped:N0} dependent action(s) skipped, {failures.Count:N0} failure(s).");
        await ShowMessageAsync("Execution Summary",
            $"Completed: {completed:N0}\nSkipped after prerequisite failure: {skipped:N0}\nFailures: {failures.Count:N0}"
            + (failures.Count == 0 ? string.Empty : "\n\n" + string.Join("\n", failures.Take(20))));
    }

    private int CountPhysicalContentLosses(BerriesSession session)
    {
        var physical = session.InitialPortrait.Files
            .Where(file => file.Content is not null)
            .Select(file => (file.Content!.Value, file.Path))
            .ToList();

        foreach (var action in session.Actions)
        {
            switch (action)
            {
                case DeleteFileAction delete:
                    physical.RemoveAll(item => fileSystem.PathsEqual(item.Path, delete.Path));
                    break;
                case MoveFileAction move:
                    var index = physical.FindIndex(item => fileSystem.PathsEqual(item.Path, move.Source));
                    if (index >= 0)
                        physical[index] = (physical[index].Value, move.Destination);
                    break;
            }
        }

        var initialContents = session.InitialPortrait.Files.Where(file => file.Content is not null)
            .Select(file => file.Content!.Value).Distinct().ToArray();
        var surviving = physical.Select(item => item.Value).ToHashSet();
        return initialContents.Count(content => !surviving.Contains(content));
    }

    private void EnsureParent(FileSystemPath path)
    {
        var parent = fileSystem.GetParentDirectory(path);
        if (parent is not null && !fileSystem.Exists(parent.Value))
            fileSystem.CreateDirectory(parent.Value);
    }

    private async Task<bool> ConfirmAsync(string title, string message, string affirmative)
    {
        var dialog = new Window { Title = title, Width = 560, Height = 260, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = new Button { Content = affirmative, MinWidth = 90 };
        var no = new Button { Content = "Cancel", MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { no, yes } };
        dialog.Content = new Grid
        {
            Margin = new Avalonia.Thickness(20),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };
        Grid.SetRow(buttons, 1);
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 650, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = new Button { Content = "Close", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
        var grid = new Grid { Margin = new Avalonia.Thickness(20), RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(new TextBox { Text = message, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        grid.Children.Add(close);
        Grid.SetRow(close, 1);
        dialog.Content = grid;
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private void ExitMenu_Click(object? sender, RoutedEventArgs e) => Close();

    private void RefreshRoots()
    {
        RootsList.ItemsSource = null;
        RootsList.ItemsSource = roots.ToArray();
        ScanButton.IsEnabled = roots.Count > 0;
        RemoveRootButton.IsEnabled = roots.Count > 0;
    }

    private void SetRootControlsEnabled(bool enabled)
    {
        AddRootButton.IsEnabled = enabled;
        RemoveRootButton.IsEnabled = enabled && roots.Count > 0;
        ScanButton.IsEnabled = enabled && roots.Count > 0;
    }

    private void BeginProgress(string text, bool indeterminate)
    {
        StatusText.Text = text;
        StatusProgress.IsVisible = true;
        StatusProgress.IsIndeterminate = indeterminate;
        StatusProgress.Value = 0;
    }

    private void EndProgress(string text)
    {
        StatusText.Text = text;
        StatusProgress.IsIndeterminate = false;
        StatusProgress.IsVisible = false;
    }

    private static string ShortContent(ContentId content) =>
        content.Value.Length <= 12 ? content.Value : content.Value[..12];
}
