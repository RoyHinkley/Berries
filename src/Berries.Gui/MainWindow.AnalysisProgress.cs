using Avalonia;
using Avalonia.Threading;
using Berries.Core;

namespace Berries.Gui;

public partial class MainWindow
{
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        controller.AnalysisProgressChanged += AnalysisProgressChanged;
        controller.AnalysisChanged += AnalysisChanged;

        // Berries owns Suggestion traversal semantics. Replace the original positional handlers
        // with identity-based traversal so a growing, re-ranked result set cannot repeat or skip items.
        SuggestButton.Click -= Suggest_Click;
        SuggestButton.Click += SuggestNextUnseen_Click;
        PivotBranchPairMenu.Click -= PivotSuggestedBranchPair_Click;
        PivotBranchPairMenu.Click += PivotCurrentSuggestion_Click;
    }

    protected override void OnClosed(EventArgs e)
    {
        controller.AnalysisProgressChanged -= AnalysisProgressChanged;
        controller.AnalysisChanged -= AnalysisChanged;
        base.OnClosed(e);
    }

    private void AnalysisProgressChanged(OperationProgress progress) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!NavigationIsActive)
                ShowAnalysisProgress(progress);
        });

    private void AnalysisChanged() =>
        Dispatcher.UIThread.Post(() =>
        {
            UpdateCapabilities();
            UpdatePivotCapabilities();
            SuggestButton.IsEnabled = HighestRankedUnseenSuggestion() is not null;
            if (controller.Suggestions is { IsComplete: true } && !portraitCommandBusy && !NavigationIsActive)
            {
                StatusProgress.IsVisible = false;
                StatusProgress.IsIndeterminate = false;
            }
        });

    private void ShowAnalysisProgress(OperationProgress progress)
    {
        StatusProgress.IsVisible = true;

        if (progress.Total is > 0 && progress.Completed is not null)
        {
            StatusProgress.IsIndeterminate = false;
            StatusProgress.Value = Math.Clamp(100.0 * progress.Completed.Value / progress.Total.Value, 0, 100);
            StatusText.Text = $"{progress.Phase} — {progress.Completed.Value:N0} / {progress.Total.Value:N0}";
        }
        else
        {
            StatusProgress.IsIndeterminate = true;
            StatusText.Text = progress.Phase + "...";
        }
    }
}
