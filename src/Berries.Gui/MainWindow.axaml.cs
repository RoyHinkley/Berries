using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Berries.Core;
using Berries.FileSystem.Windows;

namespace Berries.Gui;

public partial class MainWindow : Window
{
    private readonly GuiController controller = new(new BerriesEngine(new WindowsFileSystem()));
    private readonly List<string> roots = [];

    public MainWindow()
    {
        InitializeComponent();
        RefreshRoots();
    }

    private async void AddRootButton_Click(object? sender, RoutedEventArgs e)
    {
        var directories = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select corpus root",
            AllowMultiple = false
        });

        if (directories.Count == 0)
            return;

        var path = directories[0].TryGetLocalPath();
        if (path is null)
        {
            StatusText.Text = "The selected directory does not have a local filesystem path.";
            return;
        }

        var normalizedRoots = controller.NormalizeRoots(roots.Append(path));
        roots.Clear();
        roots.AddRange(normalizedRoots);
        RefreshRoots();
        ClearPortraitSummary();
        StatusText.Text = "Corpus changed; scan required.";
    }

    private void RemoveRootButton_Click(object? sender, RoutedEventArgs e)
    {
        if (RootsList.SelectedItem is not string selectedRoot)
            return;

        roots.Remove(selectedRoot);
        RefreshRoots();
        ClearPortraitSummary();
        StatusText.Text = roots.Count == 0 ? "No corpus selected." : "Corpus changed; scan required.";
    }

    private async void ScanButton_Click(object? sender, RoutedEventArgs e)
    {
        if (roots.Count == 0)
            return;

        SetControlsEnabled(false);
        StatusText.Text = "Scanning...";

        try
        {
            var result = await controller.ScanAsync(roots);
            FileCountText.Text = result.FileCount.ToString("N0");
            TotalBytesText.Text = result.TotalBytes.ToString("N0");
            NormalizationElapsedText.Text = FormatElapsed(result.CorpusNormalizationElapsed);
            PortraitElapsedText.Text = FormatElapsed(result.PortraitAcquisitionElapsed);
            ClearDuplicateSummary();
            StatusText.Text = $"Portrait constructed in {FormatElapsed(result.TotalElapsed)}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            SetControlsEnabled(true);
        }
    }

    private async void FindDuplicatesButton_Click(object? sender, RoutedEventArgs e)
    {
        SetControlsEnabled(false);
        StatusText.Text = "Finding duplicates...";

        try
        {
            var result = await controller.DiscoverDuplicatesAsync();
            DuplicateFileCountText.Text = result.DuplicateFileCount.ToString("N0");
            DuplicateSetCountText.Text = result.DuplicateSets.Count.ToString("N0");
            SizeGroupingElapsedText.Text = FormatElapsed(result.Timing.SizeGrouping);
            HashingElapsedText.Text = FormatElapsed(result.Timing.ContentHashing);
            StatusText.Text = $"Duplicate discovery completed in {FormatElapsed(result.Timing.Total)}; " +
                              $"set construction {FormatElapsed(result.Timing.DuplicateSetConstruction)}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            SetControlsEnabled(true);
        }
    }

    private void RefreshRoots()
    {
        RootsList.ItemsSource = null;
        RootsList.ItemsSource = roots.ToArray();
        ScanButton.IsEnabled = roots.Count > 0;
        RemoveRootButton.IsEnabled = roots.Count > 0;
        FindDuplicatesButton.IsEnabled = controller.Portrait is not null;
    }

    private void ClearPortraitSummary()
    {
        FileCountText.Text = "—";
        TotalBytesText.Text = "—";
        NormalizationElapsedText.Text = "—";
        PortraitElapsedText.Text = "—";
        ClearDuplicateSummary();
        FindDuplicatesButton.IsEnabled = false;
    }

    private void ClearDuplicateSummary()
    {
        DuplicateFileCountText.Text = "—";
        DuplicateSetCountText.Text = "—";
        SizeGroupingElapsedText.Text = "—";
        HashingElapsedText.Text = "—";
    }

    private void SetControlsEnabled(bool enabled)
    {
        AddRootButton.IsEnabled = enabled;
        RemoveRootButton.IsEnabled = enabled && roots.Count > 0;
        ScanButton.IsEnabled = enabled && roots.Count > 0;
        FindDuplicatesButton.IsEnabled = enabled && controller.Portrait is not null;
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("N3") + " s"
            : elapsed.TotalMilliseconds.ToString("N1") + " ms";
}
