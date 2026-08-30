using Avalonia.Input;
using Avalonia.Interactivity;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

public partial class MainWindow
{
    private CancellationTokenSource? portraitAnalysisRefresh;
    private Task? portraitAnalysisTask;
    private bool portraitCommandBusy;

    private async void ExcludeImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SemanticSelection();
        if (files.Count == 0 || controller.Session is null || portraitCommandBusy) return;
        await RunPortraitCommandAsync(
            $"Excluding {files.Count:N0} files...",
            $"Excluded {files.Count:N0} file(s) from the Corpus.",
            () => controller.Session.Exclude(files));
    }

    private async void DeleteImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SemanticSelection();
        if (files.Count == 0 || controller.Session is null || portraitCommandBusy) return;
        await RunPortraitCommandAsync(
            $"Scheduling deletion of {files.Count:N0} files...",
            $"Scheduled deletion of {files.Count:N0} file(s).",
            () => controller.Session.Delete(files));
    }

    private async void MoveRightImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (leftScope is null || rightScope is null || controller.Session is null || portraitCommandBusy) return;
        var descendants = ProjectionTitle.Text?.StartsWith("Branch Pair", StringComparison.Ordinal) == true;
        var files = SemanticSelectionInScope(leftScope.Value, descendants);
        if (files.Count == 0) return;
        MoveResult? result = null;
        await RunPortraitCommandAsync(
            $"Moving {files.Count:N0} files...",
            null,
            () => result = controller.Session.Move(files, leftScope.Value, rightScope.Value),
            () => MoveStatus(files.Count, result!));
    }

    private async void MoveLeftImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (leftScope is null || rightScope is null || controller.Session is null || portraitCommandBusy) return;
        var descendants = ProjectionTitle.Text?.StartsWith("Branch Pair", StringComparison.Ordinal) == true;
        var files = SemanticSelectionInScope(rightScope.Value, descendants);
        if (files.Count == 0) return;
        MoveResult? result = null;
        await RunPortraitCommandAsync(
            $"Moving {files.Count:N0} files...",
            null,
            () => result = controller.Session.Move(files, rightScope.Value, leftScope.Value),
            () => MoveStatus(files.Count, result!));
    }

    private async void UndoImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null || portraitCommandBusy) return;
        var undone = false;
        await RunPortraitCommandAsync(
            "Undoing the most recent operation...",
            null,
            () => undone = controller.Session.Undo(),
            () => undone ? "Undid the most recent operation." : "Nothing to undo.");
    }

    private IReadOnlyList<FileInstance> FastSelectedFilesFromActiveProjection() => SemanticSelection();

    private IReadOnlyList<FileInstance> FastSelectedFiles(Avalonia.Controls.TreeView tree)
    {
        if (controller.Session is not { } session) return [];
        var represented = EnumerateNodes(tree.ItemsSource).SelectMany(node => node.Files);
        var keys = DistinctFilesFast(represented).Select(file => fileSystem.NormalizePath(file.Path).Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return session.Selection.Files.Where(file => keys.Contains(fileSystem.NormalizePath(file.Path).Value)).ToArray();
    }

    private IReadOnlyList<FileInstance> DistinctFilesFast(IEnumerable<FileInstance> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FileInstance>();
        foreach (var file in files)
        {
            var key = fileSystem.NormalizePath(file.Path).Value;
            if (seen.Add(key)) result.Add(file);
        }
        return result;
    }

    private async Task RunPortraitCommandAsync(
        string busyMessage,
        string? completedMessage,
        Action command,
        Func<string>? completedMessageFactory = null)
    {
        portraitCommandBusy = true;
        BeginPortraitBusy(busyMessage);

        try
        {
            await Task.Yield();
            await StopBackgroundAnalysisAsync();
            await Task.Run(command);
            await RefreshCurrentProjectionModelsAsync();
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();

            var message = completedMessageFactory?.Invoke() ?? completedMessage ?? "Operation completed.";
            EndPortraitBusy(message + " Updating analysis in background...");
            StartBackgroundAnalysisRefresh(message);
        }
        catch (Exception ex)
        {
            EndPortraitBusy("Operation failed: " + ex.Message);
        }
        finally
        {
            portraitCommandBusy = false;
        }
    }

    private async Task RefreshCurrentProjectionModelsAsync()
    {
        var session = controller.Session;
        if (session is null) return;

        if (PairExplorer.IsVisible && leftScope is not null && rightScope is not null)
        {
            var first = leftScope.Value;
            var second = rightScope.Value;
            var isDirectoryPair = ProjectionTitle.Text?.StartsWith("Directory Pair", StringComparison.Ordinal) == true;

            if (isDirectoryPair)
            {
                var leftTask = BuildDirectoryExplorerNodeAsync(first);
                var rightTask = BuildDirectoryExplorerNodeAsync(second);
                var sharedTask = Task.Run(() => CountSharedContents(session, first, second, includeDescendants: false));
                await Task.WhenAll(leftTask, rightTask, sharedTask);
                LeftTree.ItemsSource = new[] { await leftTask };
                RightTree.ItemsSource = new[] { await rightTask };
                ProjectionTitle.Text = $"Directory Pair — {(await sharedTask):N0} shared Groups";
                return;
            }

            var rebuilt = await Task.Run(() =>
            {
                var left = BuildBranchTree(first);
                var right = BuildBranchTree(second);
                var shared = CountSharedContents(session, first, second, includeDescendants: true);
                return (Left: left, Right: right, Shared: shared);
            });

            LeftTree.ItemsSource = new[] { rebuilt.Left };
            RightTree.ItemsSource = new[] { rebuilt.Right };
            ProjectionTitle.Text = $"Branch Pair — {rebuilt.Shared:N0} shared Groups";
            return;
        }

        if (currentScope is not null)
        {
            var scope = currentScope.Value;
            if (!scopeIncludesDescendants)
            {
                ExplorerTree.ItemsSource = new[] { await BuildDirectoryExplorerNodeAsync(scope) };
                return;
            }

            var node = await Task.Run(() => BuildBranchTree(scope));
            ExplorerTree.ItemsSource = new[] { node };
            return;
        }

        var groups = await Task.Run(() => session.DuplicateSets
            .OrderByDescending(set => set.Files.Count)
            .ThenBy(set => set.Files[0].Path.Value, StringComparer.OrdinalIgnoreCase)
            .Select(set => BuildGroupNode(set.Files))
            .ToArray());
        ExplorerTree.ItemsSource = groups;
    }

    private int CountSharedContents(BerriesSession session, FileSystemPath first, FileSystemPath second, bool includeDescendants)
    {
        static bool InScope(IFileSystem fs, FileInstance file, FileSystemPath scope, bool descendants) =>
            fs.PathsEqual(file.ParentDirectory, scope) || (descendants && fs.IsDescendant(file.ParentDirectory, scope));

        var count = 0;
        foreach (var set in session.DuplicateSets)
        {
            var inFirst = false;
            var inSecond = false;
            foreach (var file in set.Files)
            {
                if (!inFirst && InScope(fileSystem, file, first, includeDescendants)) inFirst = true;
                if (!inSecond && InScope(fileSystem, file, second, includeDescendants)) inSecond = true;
                if (inFirst && inSecond) break;
            }
            if (inFirst && inSecond) count++;
        }
        return count;
    }

    private void BeginPortraitBusy(string message)
    {
        StatusText.Text = message;
        StatusProgress.IsVisible = true;
        StatusProgress.IsIndeterminate = true;
        Cursor = new Cursor(StandardCursorType.Wait);
        ExplorerPanel.IsEnabled = false;
        MainMenu.IsEnabled = false;
    }

    private void EndPortraitBusy(string message)
    {
        StatusText.Text = message;
        StatusProgress.IsVisible = false;
        StatusProgress.IsIndeterminate = false;
        Cursor = null;
        ExplorerPanel.IsEnabled = true;
        MainMenu.IsEnabled = true;
    }

    private async Task StopBackgroundAnalysisAsync()
    {
        var refresh = portraitAnalysisRefresh;
        var task = portraitAnalysisTask;
        if (refresh is null || task is null) return;

        refresh.Cancel();
        try { await task; }
        catch (OperationCanceledException) { }
        finally
        {
            if (refresh == portraitAnalysisRefresh)
            {
                refresh.Dispose();
                portraitAnalysisRefresh = null;
                portraitAnalysisTask = null;
            }
        }
    }

    private void StartBackgroundAnalysisRefresh(string completedMessage)
    {
        portraitAnalysisRefresh?.Cancel();
        portraitAnalysisRefresh?.Dispose();
        portraitAnalysisRefresh = new CancellationTokenSource();
        var refresh = portraitAnalysisRefresh;
        portraitAnalysisTask = RefreshAnalysisGenerationAsync(refresh, completedMessage);
    }

    private async Task RefreshAnalysisGenerationAsync(CancellationTokenSource refresh, string completedMessage)
    {
        try
        {
            await Task.Run(() => controller.RefreshAnalysisAsync(refresh.Token), refresh.Token);
            if (refresh != portraitAnalysisRefresh) return;
            StatusText.Text = completedMessage;
            StatusProgress.IsVisible = false;
            StatusProgress.IsIndeterminate = false;
            UpdateCapabilities();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (refresh != portraitAnalysisRefresh) return;
            StatusText.Text = completedMessage + " Background analysis update failed: " + ex.Message;
            StatusProgress.IsVisible = false;
            StatusProgress.IsIndeterminate = false;
        }
        finally
        {
            if (refresh == portraitAnalysisRefresh)
            {
                refresh.Dispose();
                portraitAnalysisRefresh = null;
                portraitAnalysisTask = null;
            }
        }
    }
}
