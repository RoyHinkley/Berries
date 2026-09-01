using System.Diagnostics;
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
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow : Window
{
    private readonly WindowsFileSystem fileSystem = new();
    private readonly BerriesApplication controller;
    private readonly FileActionExecutor fileActionExecutor;
    private readonly List<string> roots = [];
    private int suggestionIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        var engine = new BerriesEngine(fileSystem);
        controller = new BerriesApplication(
            fileSystem,
            engine,
            new BranchStatisticsAnalyzer(fileSystem),
            new BranchCounterpartAnalyzer(fileSystem));
        fileActionExecutor = new FileActionExecutor(fileSystem);
        var savedRoots = RecentRootsStore.Load();
        if (savedRoots.Count > 0)
            roots.AddRange(controller.NormalizeRoots(savedRoots));
        RefreshRoots();
    }

    private async void AddRootButton_Click(object? sender, RoutedEventArgs e)
    {
        var directories = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select root",
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
        StatusText.Text = "Roots changed; scan required.";
    }

    private void RemoveRootButton_Click(object? sender, RoutedEventArgs e)
    {
        if (RootsList.SelectedItem is not string selectedRoot) return;
        roots.Remove(selectedRoot);
        RefreshRoots();
        StatusText.Text = roots.Count == 0
            ? "Select roots to begin."
            : "Roots changed; scan required.";
    }

    private void SelectRootsMenu_Click(object? sender, RoutedEventArgs e)
    {
        RootsPanel.IsVisible = true;
        ExplorerPanel.IsVisible = false;
        StatusText.Text = roots.Count == 0
            ? "Select roots to begin."
            : "Edit roots, then Explore to start a new session.";
    }

    private async void ScanButton_Click(object? sender, RoutedEventArgs e)
    {
        if (roots.Count == 0) return;
        SetRootControlsEnabled(false);
        ShowExplorerWithRoots();
        BeginProgress("Scanning roots...", true);
        try
        {
            suggestionIndex = -1;

            var config = BerriesConfig.Load(Path.Combine(AppContext.BaseDirectory, "Berries.config"));
            var scanProgress = new Progress<ScanProgress>(p =>
                StatusText.Text = $"Scanning roots — {p.FilesExamined:N0} files");
            var groupProgress = new Progress<GroupDiscoveryProgress>(p =>
            {
                var completed = p.Completed ?? p.FilesHashed;
                var total = p.Total ?? p.CandidateFiles;
                StatusText.Text = total > 0
                    ? $"{p.Phase} — {completed:N0} / {total:N0}"
                    : p.Phase + "...";
                StatusProgress.IsIndeterminate = total <= 0;
                if (total > 0)
                    StatusProgress.Value = 100.0 * completed / total;
            });

            var scanTask = controller.ScanAsync(roots, config.IsExcluded, scanProgress, groupProgress);
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();

            var scan = await scanTask;
            await ShowContentProjectionAsync();
            _ = PrewarmCorpusRootsAsync();
            EndProgress(
                $"Ready — {scan.FileCount:N0} files, with {scan.GroupedFileCount:N0} files in {scan.GroupCount:N0} Groups."
                + (scan.EvictionCount == 0
                    ? string.Empty
                    : $" {scan.EvictionCount:N0} inaccessible file(s) omitted."));
            UpdateCapabilities();
        }
        catch (Exception ex)
        {
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            EndProgress(ex.Message);
        }
        finally
        {
            SetRootControlsEnabled(true);
        }
    }

    private async Task PrewarmCorpusRootsAsync()
    {
        var session = controller.Session;
        var corpus = controller.Corpus;
        if (session is null || corpus is null) return;

        var portrait = session.WorkingPortrait;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await Projections.CorpusRootsAsync(session, corpus);
            if (ReferenceEquals(controller.Session?.WorkingPortrait, portrait))
                Debug.WriteLine($"[Berries] Corpus Roots projection prewarmed in {stopwatch.Elapsed.TotalMilliseconds:N1} ms.");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Berries] Corpus Roots projection prewarm canceled.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Berries] Corpus Roots projection prewarm failed: {ex.Message}");
        }
    }

    private void ShowExplorerWithRoots()
    {
        RootsPanel.IsVisible = false;
        ExplorerPanel.IsVisible = true;
        PairExplorer.IsVisible = false;
        SingleExplorer.IsVisible = true;
        SetProjectionState(ProjectionKind.Corpus, []);
        ProjectionTitle.Text = "Roots";
        ExplorerTree.ItemsSource = roots
            .Select(root => new ExplorerNode(root, semanticPath: new FileSystemPath(root)))
            .ToArray();
    }

    private async Task ShowContentProjectionAsync()
    {
        var session = controller.Session;
        if (session is null) return;

        var operation = BeginNavigation("Building Groups view...", true);
        try
        {
            var groups = await Projections.GroupsAsync(
                session,
                new Progress<OperationProgress>(progress => ShowNavigationProgress(operation, progress)),
                operation.Token);
            operation.Mark("Groups projection acquired");
            operation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.Groups, groups.SelectMany(group => group.Files));
            operation.Mark("Groups projection state set");
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = "Groups";

            var cache = GetGroupsExplorerCache(session.WorkingPortrait);
            ExplorerTree.ItemsSource = cache.Nodes;
            operation.Mark($"Groups ItemsSource assigned ({cache.Nodes.Count:N0} cached nodes)");
            await Task.Yield();
            operation.Mark("Groups first UI yield returned");
            await BuildGroupsExplorerTreeAsync(
                groups,
                cache.Nodes,
                cache.BuiltCount,
                operation,
                completed => cache.BuiltCount = completed);
            operation.Mark("Groups tree available");

            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            SynchronizeVisibleSelection();
            operation.Mark("Groups selection synchronized");
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            operation.Mark("Groups capabilities updated");
            operation.MarkWhenUiSettled("Groups UI reached Background priority");
            CompleteNavigation(operation, "Groups");
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

    private async Task ShowBranchPairAsync(BranchPairSuggestion suggestion)
    {
        if (controller.Session is null || suggestion.Counterparts.Count == 0) return;
        var first = suggestion.Seed.Branch.Path;
        var second = suggestion.Counterparts[0].Branch.Path;
        var sharedGroupCount = suggestion.Counterparts[0].SharedGroupCount;
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

    private static IEnumerable<ExplorerNode> EnumerateNodes(System.Collections.IEnumerable? items)
    {
        if (items is null) yield break;
        foreach (var node in items.OfType<ExplorerNode>())
        {
            yield return node;
            foreach (var child in EnumerateNodes(node.Children)) yield return child;
        }
    }

    private static string MoveStatus(int selectedCount, MoveResult result)
    {
        var collisionCount = result.Collisions.Count;
        var movedCount = selectedCount - collisionCount;
        var text = $"Move requested for {selectedCount:N0} file(s): {movedCount:N0} moved";
        if (collisionCount > 0) text += $", {collisionCount:N0} conflict(s) skipped";
        return text + ".";
    }

    private void UpdateCapabilities()
    {
        var session = controller.Session;
        var hasSession = session is not null;
        var hasPair = currentProjection is { IsPair: true, Primary: not null, Secondary: not null };
        MoveRightButton.IsEnabled = hasPair;
        MoveLeftButton.IsEnabled = hasPair;
        UndoButton.IsEnabled = hasSession && session!.Operations.Count > 0;
        ExecuteMenu.IsEnabled = hasSession && session!.Actions.Count > 0;
        var suggestions = controller.Suggestions?.Suggestions;
        SuggestButton.IsEnabled = suggestions is { Count: > 0 };
        PivotBranchPairMenu.IsEnabled = suggestionIndex >= 0 && suggestions is { Count: > 0 };
    }

    private async void ExecuteMenu_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session;
        if (session is null || session.Actions.Count == 0) return;

        var contentLosses = fileActionExecutor.CountPhysicalContentLosses(session);
        var approved = await ConfirmAsync(
            "Execute filesystem changes?",
            $"Planned filesystem actions: {session.Actions.Count:N0}\nGroups with no surviving physical file after the plan: {contentLosses:N0}\n\nBerries will continue past independent failures and report the results.",
            "Execute");
        if (!approved) return;

        BeginPortraitBusy("Executing filesystem actions...");
        try
        {
            var result = await fileActionExecutor.ExecuteAsync(session.Actions);
            EndPortraitBusy(
                $"Execution finished — {result.CompletedCount:N0} completed, {result.SkippedCount:N0} dependent action(s) skipped, {result.Failures.Count:N0} failure(s)."
                + (result.Failures.Count == 0 ? string.Empty : " See the failure summary."));
            if (result.Failures.Count > 0)
            {
                var failures = result.Failures.Take(50)
                    .Select(failure => $"{DescribeAction(failure.Action)} — {failure.Message}");
                await ShowMessageAsync(
                    "Execution failures",
                    string.Join(Environment.NewLine + Environment.NewLine, failures));
            }
        }
        catch (Exception ex)
        {
            EndPortraitBusy("Execution failed: " + ex.Message);
        }
    }

    private static string DescribeAction(FileAction action) => action switch
    {
        DeleteFileAction delete => $"Delete {delete.Path.Value}",
        CopyFileAction copy => $"Copy {copy.Source.Value} -> {copy.Destination.Value}",
        MoveFileAction move => $"Move {move.Source.Value} -> {move.Destination.Value}",
        _ => action.ToString() ?? action.GetType().Name
    };

    private async Task<bool> ConfirmAsync(string title, string message, string affirmative)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Content = BuildDialogContent(message, affirmative, out var yes, out var no)
        };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool>(this);
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

    private static Control BuildDialogContent(
        string message,
        string affirmative,
        out Button yes,
        out Button no)
    {
        yes = new Button { Content = affirmative };
        no = new Button { Content = "Cancel" };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(no);
        buttons.Children.Add(yes);
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 18
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(buttons);
        return panel;
    }

    private void SetRootControlsEnabled(bool enabled)
    {
        AddRootButton.IsEnabled = enabled;
        RemoveRootButton.IsEnabled = enabled;
        ScanButton.IsEnabled = enabled;
    }

    private void BeginProgress(string text, bool indeterminate)
    {
        StatusText.Text = text;
        StatusProgress.IsVisible = true;
        StatusProgress.IsIndeterminate = indeterminate;
        if (!indeterminate) StatusProgress.Value = 0;
    }

    private void EndProgress(string text)
    {
        StatusText.Text = text;
        StatusProgress.IsVisible = false;
        StatusProgress.IsIndeterminate = false;
    }

    private void RefreshRoots()
    {
        RootsList.ItemsSource = null;
        RootsList.ItemsSource = roots.ToArray();
        RecentRootsStore.Save(roots);
    }

    private void ExitMenu_Click(object? sender, RoutedEventArgs e) => Close();
}

public sealed class ExplorerNode(
    string label,
    IReadOnlyList<FileInstance>? files = null,
    FileSystemPath? semanticPath = null)
{
    public string Label { get; } = label;
    public IReadOnlyList<FileInstance> Files { get; set; } = files ?? [];
    public FileSystemPath? SemanticPath { get; } = semanticPath;
    public List<ExplorerNode> Children { get; } = [];
}
