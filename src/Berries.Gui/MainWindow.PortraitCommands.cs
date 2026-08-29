using Avalonia.Interactivity;

namespace Berries.Gui;

public partial class MainWindow
{
    private CancellationTokenSource? portraitAnalysisRefresh;

    private void ExcludeImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection();
        if (files.Count == 0 || controller.Session is null) return;
        controller.Session.Exclude(files);
        CompletePortraitCommand($"Excluded {files.Count:N0} file(s) from the Corpus.");
    }

    private void DeleteImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        var files = SelectedFilesFromActiveProjection();
        if (files.Count == 0 || controller.Session is null) return;
        controller.Session.Delete(files);
        CompletePortraitCommand($"Scheduled deletion of {files.Count:N0} file(s).");
    }

    private void MoveRightImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (leftScope is null || rightScope is null || controller.Session is null) return;
        var files = SelectedFiles(LeftTree);
        if (files.Count == 0) return;
        var result = controller.Session.Move(files, leftScope.Value, rightScope.Value);
        CompletePortraitCommand(MoveStatus(files.Count, result));
    }

    private void MoveLeftImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (leftScope is null || rightScope is null || controller.Session is null) return;
        var files = SelectedFiles(RightTree);
        if (files.Count == 0) return;
        var result = controller.Session.Move(files, rightScope.Value, leftScope.Value);
        CompletePortraitCommand(MoveStatus(files.Count, result));
    }

    private void UndoImmediateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session?.Undo() != true) return;
        CompletePortraitCommand("Undid the most recent operation.");
    }

    private void CompletePortraitCommand(string message)
    {
        // The Working Portrait is authoritative and changes synchronously. Reflect it in
        // the Explorer immediately; derived directory/branch/suggestion analysis may lag.
        RefreshCurrentProjection();
        UpdateCapabilities();
        StatusText.Text = message + " Updating suggestions in background...";
        StartBackgroundAnalysisRefresh(message);
    }

    private async void StartBackgroundAnalysisRefresh(string completedMessage)
    {
        portraitAnalysisRefresh?.Cancel();
        portraitAnalysisRefresh?.Dispose();
        portraitAnalysisRefresh = new CancellationTokenSource();
        var refresh = portraitAnalysisRefresh;

        try
        {
            await controller.RefreshAnalysisAsync(refresh.Token);
            if (refresh != portraitAnalysisRefresh) return;
            StatusText.Text = completedMessage;
            UpdateCapabilities();
        }
        catch (OperationCanceledException)
        {
            // A newer portrait command superseded this analysis generation.
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
                portraitAnalysisRefresh.Dispose();
                portraitAnalysisRefresh = null;
            }
        }
    }
}
