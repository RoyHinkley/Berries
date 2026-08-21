using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Berries.Core;
using Berries.Core.Analysis;
using Berries.Core.Cases;
using Berries.FileSystem.Windows;

namespace Berries.Gui;

public partial class MainWindow : Window
{
    private readonly GuiController controller;
    private readonly StructuralEvidenceAnalyzer evidenceAnalyzer;
    private readonly List<string> roots = [];

    public MainWindow()
    {
        InitializeComponent();
        var fileSystem = new WindowsFileSystem();
        controller = new GuiController(new BerriesEngine(fileSystem), new CaseAnalyzer(fileSystem));
        evidenceAnalyzer = new StructuralEvidenceAnalyzer(fileSystem);
        RefreshRoots();
    }

    private async void AddRootButton_Click(object? sender, RoutedEventArgs e)
    {
        var directories = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select corpus root", AllowMultiple = false });
        if (directories.Count == 0) return;
        var path = directories[0].TryGetLocalPath();
        if (path is null) { StatusText.Text = "The selected directory does not have a local filesystem path."; return; }
        var normalizedRoots = controller.NormalizeRoots(roots.Append(path));
        roots.Clear(); roots.AddRange(normalizedRoots); RefreshRoots(); ClearPortraitSummary();
        StatusText.Text = "Corpus changed; scan required.";
    }

    private void RemoveRootButton_Click(object? sender, RoutedEventArgs e)
    {
        if (RootsList.SelectedItem is not string selectedRoot) return;
        roots.Remove(selectedRoot); RefreshRoots(); ClearPortraitSummary();
        StatusText.Text = roots.Count == 0 ? "No corpus selected." : "Corpus changed; scan required.";
    }

    private async void ScanButton_Click(object? sender, RoutedEventArgs e)
    {
        if (roots.Count == 0) return;

        SetControlsEnabled(false);
        ClearPortraitSummary();

        try
        {
            StatusText.Text = "Scanning corpus...";
            var scan = await controller.ScanAsync(roots);
            FileCountText.Text = scan.FileCount.ToString("N0");
            TotalBytesText.Text = scan.TotalBytes.ToString("N0");
            NormalizationElapsedText.Text = FormatElapsed(scan.CorpusNormalizationElapsed);
            PortraitElapsedText.Text = FormatElapsed(scan.PortraitAcquisitionElapsed);

            StatusText.Text = "Finding duplicates...";
            var duplicates = await controller.DiscoverDuplicatesAsync();
            DuplicateFileCountText.Text = duplicates.DuplicateFileCount.ToString("N0");
            DuplicateSetCountText.Text = duplicates.DuplicateSets.Count.ToString("N0");
            SizeGroupingElapsedText.Text = FormatElapsed(duplicates.Timing.SizeGrouping);
            HashingElapsedText.Text = FormatElapsed(duplicates.Timing.ContentHashing);
            FileCountText.Text = duplicates.Portrait.Files.Count.ToString("N0");
            TotalBytesText.Text = duplicates.Portrait.Files.Sum(file => file.Length).ToString("N0");

            StatusText.Text = "Screening distributed duplicate sets...";
            var candidates = controller.FindSprinkledDuplicateCandidates();
            IReadOnlyList<SprinkledDuplicateCandidate> accepted = [];
            if (candidates.Count > 0)
            {
                var dialog = new SprinkledDuplicateDialog(candidates);
                var selection = await dialog.ShowDialog<IReadOnlyList<SprinkledDuplicateCandidate>?>(this);
                if (selection is null)
                {
                    StatusText.Text = "Analysis canceled during duplicate settlement review.";
                    return;
                }

                accepted = selection;
                controller.AcceptWholeDuplicateSets(accepted);
            }

            StatusText.Text = "Analyzing directory relationships...";
            var directories = await controller.AnalyzeDirectoriesAsync();
            DirectoryCountText.Text = directories.Directories.Count.ToString("N0");
            DirectoryPairCountText.Text = directories.DirectoryPairs.Count.ToString("N0");
            DirectoryAnalysisElapsedText.Text = FormatElapsed(directories.Timing.Total);
            DirectoryPairsList.ItemsSource = directories.DirectoryPairs.Take(25)
                .Select(pair => $"{pair.Leverage,6:N0}    {pair.First.Value}    ↔    {pair.Second.Value}")
                .ToArray();

            StatusText.Text = "Analyzing branch relationships...";
            var branches = await controller.AnalyzeBranchesAsync();
            BranchPairCountText.Text = branches.BranchPairs.Count.ToString("N0");
            BranchAnalysisElapsedText.Text = FormatElapsed(branches.Timing.Total);
            BranchPairsList.ItemsSource = branches.BranchPairs.Take(25)
                .Select(pair => $"{pair.Leverage,6:N0}  [{pair.DirectoryPairCount,5:N0}]    {pair.FirstRoot.Value}    ↔    {pair.SecondRoot.Value}")
                .ToArray();

            StatusText.Text = "Ranking cases...";
            var cases = controller.AnalyzeTopCases(25);
            var duplicateDiscovery = controller.DuplicateDiscovery
                ?? throw new InvalidOperationException("Duplicate discovery has not completed.");
            var report = CaseReportFormatter.Format(
                scan,
                duplicateDiscovery,
                cases,
                controller.Portrait!.Files,
                duplicateDiscovery.DuplicateSets,
                directories,
                branches,
                evidenceAnalyzer);

            report += FormatEarlySettlementSummary(candidates, accepted);
            CasesReportText.Text = report;

            var evictionText = duplicates.Evictions.Count == 0
                ? string.Empty
                : $" {duplicates.Evictions.Count:N0} inaccessible file(s) removed from the portrait.";
            StatusText.Text = $"Analysis complete: {cases.TotalCaseCount:N0} cases after {accepted.Count:N0} accepted distributed DuplicateSet(s); showing top {cases.TopCases.Count:N0}." + evictionText;
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

    private static string FormatEarlySettlementSummary(
        IReadOnlyList<SprinkledDuplicateCandidate> candidates,
        IReadOnlyList<SprinkledDuplicateCandidate> accepted)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Early distributed-DuplicateSet review");
        builder.AppendLine($"  screened candidates: {candidates.Count:N0}");
        builder.AppendLine($"  accepted retain-all settlements: {accepted.Count:N0}");

        foreach (var candidate in accepted)
            builder.AppendLine($"    {candidate.FileName}  ({candidate.DirectoryCount:N0} folders)");

        return builder.ToString();
    }

    private void RefreshRoots()
    {
        RootsList.ItemsSource = null;
        RootsList.ItemsSource = roots.ToArray();
        ScanButton.IsEnabled = roots.Count > 0;
        RemoveRootButton.IsEnabled = roots.Count > 0;
    }

    private void ClearPortraitSummary()
    {
        FileCountText.Text = "—";
        TotalBytesText.Text = "—";
        NormalizationElapsedText.Text = "—";
        PortraitElapsedText.Text = "—";
        DuplicateFileCountText.Text = "—";
        DuplicateSetCountText.Text = "—";
        SizeGroupingElapsedText.Text = "—";
        HashingElapsedText.Text = "—";
        DirectoryCountText.Text = "—";
        DirectoryPairCountText.Text = "—";
        DirectoryAnalysisElapsedText.Text = "—";
        BranchPairCountText.Text = "—";
        BranchAnalysisElapsedText.Text = "—";
        DirectoryPairsList.ItemsSource = null;
        BranchPairsList.ItemsSource = null;
        CasesReportText.Text = string.Empty;
    }

    private void SetControlsEnabled(bool enabled)
    {
        AddRootButton.IsEnabled = enabled;
        RemoveRootButton.IsEnabled = enabled && roots.Count > 0;
        ScanButton.IsEnabled = enabled && roots.Count > 0;
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("N3") + " s"
            : elapsed.TotalMilliseconds.ToString("N1") + " ms";
}
