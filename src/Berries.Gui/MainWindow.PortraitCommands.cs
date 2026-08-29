using Avalonia.Input;
using Avalonia.Interactivity;

namespace Berries.Gui;

public partial class MainWindow
{
    private CancellationTokenSource? portraitAnalysisRefresh;
    private Task? portraitAnalysisTask;
    private bool portraitCommandBusy;

    private async void ExcludeImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection();
        if (files.Count == 0 || controller.Session is null || portraitCommandBusy) return;
        await RunPortraitCommandAsync(
            $"Excluding {files.Count:N0} files...",
            $"Excluded {files.Count:N0} file(s) from the Corpus.",
            () => controller.Session.Exclude(files));
    }

    private async void DeleteImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection();
        if (files.Count == 0 || controller.Session is null || portraitCommandBusy) return;
        await RunPortraitCommandAsync(
            $"Scheduling deletion of {files.Count:N0} files...",
            $"Scheduled deletion of {files.Count:N0} file(s).",
            () => controller.Session.Delete(files));
    }

    private async void MoveRightImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (leftScope is null || rightScope is null || controller.Session is null || portraitCommandBusy) return;
        var files = SelectedFiles(LeftTree);
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
        var files = SelectedFiles(RightTree);
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
            await StopBackgroundAnalysisAsync();
            await Task.Run(command);

            // Only the small presentation update belongs on the UI thread. The Explorer
            // is data-bound, so this replaces lightweight node models rather than eagerly
            // constructing thousands of Avalonia controls.
            RefreshCurrentProjection();
            UpdateCapabilities();

            var message = completedMessageFactory?.Invoke() ?? completedMessage ?? "Operation completed.";
            EndPortraitBusy(message + " Updating suggestions in background...");
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
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
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
            // Force all analysis work away from the UI thread, including any synchronous
            // work performed before the analyzer reaches its first await.
            await Task.Run(() => controller.RefreshAnalysisAsync(refresh.Token), refresh.Token);
            if (refresh != portraitAnalysisRefresh) return;
            StatusText.Text = completedMessage;
            UpdateCapabilities();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (refresh != portraitAnalysisRefresh) return;
            StatusText.Text = completedMessage + " Background analysis update failed: " + ex.Message;
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
