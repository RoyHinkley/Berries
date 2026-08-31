using Avalonia.Input;
using Avalonia.Interactivity;
using Berries.Core.Domain;
using Berries.Projection;

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
            () => controller.ExcludeAsync(files));
    }

    private async void DeleteImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SemanticSelection();
        if (files.Count == 0 || controller.Session is null || portraitCommandBusy) return;
        await RunPortraitCommandAsync(
            $"Scheduling deletion of {files.Count:N0} files...",
            $"Scheduled deletion of {files.Count:N0} file(s).",
            () => controller.DeleteAsync(files));
    }

    private async void MoveRightImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (currentCase is not { IsPair: true, Primary: { } first, Secondary: { } second }
            || controller.Session is null || portraitCommandBusy) return;
        var descendants = currentCase.Kind == ProjectionKind.BranchPair;
        var files = SemanticSelectionInScope(first, descendants);
        if (files.Count == 0) return;
        MoveResult? result = null;
        await RunPortraitCommandAsync(
            $"Moving {files.Count:N0} files...",
            null,
            async () => { result = await controller.MoveAsync(files, first, second); },
            () => MoveStatus(files.Count, result!));
    }

    private async void MoveLeftImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (currentCase is not { IsPair: true, Primary: { } first, Secondary: { } second }
            || controller.Session is null || portraitCommandBusy) return;
        var descendants = currentCase.Kind == ProjectionKind.BranchPair;
        var files = SemanticSelectionInScope(second, descendants);
        if (files.Count == 0) return;
        MoveResult? result = null;
        await RunPortraitCommandAsync(
            $"Moving {files.Count:N0} files...",
            null,
            async () => { result = await controller.MoveAsync(files, second, first); },
            () => MoveStatus(files.Count, result!));
    }

    private async void UndoImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null || portraitCommandBusy) return;
        var undone = false;
        await RunPortraitCommandAsync(
            "Undoing the most recent operation...",
            null,
            async () => { undone = await controller.UndoAsync(); },
            () => undone ? "Undid the most recent operation." : "Nothing to undo.");
    }

    private IReadOnlyList<FileInstance> DistinctFilesFast(IEnumerable<FileInstance> files) =>
        Projections.DistinctFiles(files);

    private async Task RunPortraitCommandAsync(
        string busyMessage,
        string? completedMessage,
        Func<Task> command,
        Func<string>? completedMessageFactory = null)
    {
        portraitCommandBusy = true;
        BeginPortraitBusy(busyMessage);
        try
        {
            await StopBackgroundAnalysisAsync();
            await command();
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
        if (session is null || currentCase is null) return;

        if (currentCase is { Kind: ProjectionKind.DirectoryPair, Primary: { } firstDirectory, Secondary: { } secondDirectory })
        {
            var leftTask = BuildDirectoryExplorerNodeAsync(firstDirectory);
            var rightTask = BuildDirectoryExplorerNodeAsync(secondDirectory);
            var sharedTask = Projections.SharedGroupCountAsync(session, firstDirectory, secondDirectory, includeDescendants: false);
            await Task.WhenAll(leftTask, rightTask, sharedTask);
            var left = await leftTask; var right = await rightTask;
            LeftTree.ItemsSource = new[] { left }; RightTree.ItemsSource = new[] { right };
            SetPairProjectionState(ProjectionKind.DirectoryPair, firstDirectory, left.Files, secondDirectory, right.Files);
            ProjectionTitle.Text = $"Directory Pair — {(await sharedTask):N0} shared Groups";
            return;
        }

        if (currentCase is { Kind: ProjectionKind.BranchPair, Primary: { } firstBranch, Secondary: { } secondBranch })
        {
            var leftTask = BuildBranchExplorerNodeAsync(firstBranch);
            var rightTask = BuildBranchExplorerNodeAsync(secondBranch);
            var sharedTask = Projections.SharedGroupCountAsync(session, firstBranch, secondBranch, includeDescendants: true);
            await Task.WhenAll(leftTask, rightTask, sharedTask);
            var left = await leftTask; var right = await rightTask;
            LeftTree.ItemsSource = new[] { left }; RightTree.ItemsSource = new[] { right };
            SetPairProjectionState(ProjectionKind.BranchPair, firstBranch, left.Files, secondBranch, right.Files);
            ProjectionTitle.Text = $"Branch Pair — {(await sharedTask):N0} shared Groups";
            return;
        }

        if (currentCase is { Kind: ProjectionKind.Directory, Primary: { } directory })
        {
            var node = await BuildDirectoryExplorerNodeAsync(directory);
            ExplorerTree.ItemsSource = new[] { node };
            SetProjectionState(ProjectionKind.Directory, node.Files, directory);
            return;
        }

        if (currentCase is { Kind: ProjectionKind.Branch, Primary: { } branch })
        {
            var node = await BuildBranchExplorerNodeAsync(branch);
            ExplorerTree.ItemsSource = new[] { node };
            SetProjectionState(ProjectionKind.Branch, node.Files, branch);
            return;
        }

        if (currentCase.Kind == ProjectionKind.Groups)
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
