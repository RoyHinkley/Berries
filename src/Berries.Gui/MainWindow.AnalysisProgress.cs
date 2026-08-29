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
    }

    protected override void OnClosed(EventArgs e)
    {
        controller.AnalysisProgressChanged -= AnalysisProgressChanged;
        base.OnClosed(e);
    }

    private void AnalysisProgressChanged(OperationProgress progress) =>
        Dispatcher.UIThread.Post(() => ShowAnalysisProgress(progress));

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
