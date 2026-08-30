using Avalonia.Input;
using Avalonia.Interactivity;
using Berries.Core.Domain;

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
        var descendants = currentProjection?.Kind == ProjectionKind.BranchPair;
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
        var descendants = currentProjection?.Kind == ProjectionKind.BranchPair;
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
        catch (Exception ex) { EndPortraitBusy("Operation failed: " + ex.Message); }
        finally { portraitCommandBusy = false; }
    }

    private async Task RefreshCurrentProjectionModelsAsync()
    {
        var session = controller.Session;
        if (session is null || currentProjection is null) return;

        if (currentProjection.Kind == ProjectionKind.DirectoryPair && leftScope is not null && rightScope is not null)
        {
            var first = leftScope.Value; var second = rightScope.Value;
            var leftTask = BuildDirectoryExplorerNodeAsync(first);
            var rightTask = BuildDirectoryExplorerNodeAsync(second);
            var sharedTask = Projections.SharedGroupCountAsync(session, first, second, includeDescendants: false);
            await Task.WhenAll(leftTask, rightTask, sharedTask);
            var left = await leftTask; var right = await rightTask;
            LeftTree.ItemsSource = new[] { left }; RightTree.ItemsSource = new[] { right };
            SetPairProjectionState(ProjectionKind.DirectoryPair, first, left.Files, second, right.Files);
            ProjectionTitle.Text = $"Directory Pair — {(await sharedTask):N0} shared Groups";
            return;
        }

        if (currentProjection.Kind == ProjectionKind.BranchPair && leftScope is not null && rightScope is not null)
        {
            var first = leftScope.Value; var second = rightScope.Value;
            var leftTask = BuildBranchExplorerNodeAsync(first);
            var rightTask = BuildBranchExplorerNodeAsync(second);
            var sharedTask = Projections.SharedGroupCountAsync(session, first, second, includeDescendants: true);
            await Task.WhenAll(leftTask, rightTask, sharedTask);
            var left = await leftTask; var right = await rightTask;
            LeftTree.ItemsSource = new[] { left }; RightTree.ItemsSource = new[] { right };
            SetPairProjectionState(ProjectionKind.BranchPair, first, left.Files, second, right.Files);
            ProjectionTitle.Text = $"Branch Pair — {(await sharedTask):N0} shared Groups";
            return;
        }

        if (currentProjection.Kind == ProjectionKind.Directory && currentScope is not null)
        {
            var node = await BuildDirectoryExplorerNodeAsync(currentScope.Value);
            ExplorerTree.ItemsSource = new[] { node };
            SetProjectionState(ProjectionKind.Directory, node.Files, currentScope);
            return;
        }

        if (currentProjection.Kind == ProjectionKind.Branch && currentScope is not null)
        {
            var node = await BuildBranchExplorerNodeAsync(currentScope.Value);
            ExplorerTree.ItemsSource = new[] { node };
            SetProjectionState(ProjectionKind.Branch, node.Files, currentScope);
            return;
        }

        if (currentProjection.Kind == ProjectionKind.Groups)
        {
            var groups = Projections.Groups(session);
            ExplorerTree.ItemsSource = groups.Select(BuildGroupNode).ToArray();
            SetProjectionState(ProjectionKind.Groups, groups.SelectMany(group => group.Files));
        }
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
            await controller.RefreshAnalysisAsync(refresh.Token);
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
