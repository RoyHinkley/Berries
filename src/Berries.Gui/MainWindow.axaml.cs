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
    private readonly BranchCounterpartAnalyzer counterpartAnalyzer;
    private readonly List<string> roots = [];

    public MainWindow()
    {
        InitializeComponent();
        var fileSystem = new WindowsFileSystem();
        controller = new GuiController(
            new BerriesEngine(fileSystem),
            new CaseAnalyzer(fileSystem),
            new BranchStatisticsAnalyzer(fileSystem));
        counterpartAnalyzer = new BranchCounterpartAnalyzer(fileSystem);
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
            var configPath = Path.Combine(AppContext.BaseDirectory, "Berries.config");
            var config = BerriesConfig.Load(configPath);

            StatusText.Text = config.IgnorePatterns.Count == 0
                ? "Scanning corpus..."
                : $"Scanning corpus with {config.IgnorePatterns.Count:N0} ignore rule(s)...";
            var scan = await controller.ScanAsync(roots, config.IsIgnored);
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

            StatusText.Text = "Analyzing directory and branch statistics...";
            var directories = await controller.AnalyzeDirectoriesAsync();
            var branchStatistics = controller.BranchStatistics
                ?? throw new InvalidOperationException("Branch statistics have not completed.");
            DirectoryCountText.Text = directories.Directories.Count.ToString("N0");
            DirectoryPairCountText.Text = directories.DirectoryPairs.Count.ToString("N0");
            DirectoryAnalysisElapsedText.Text = FormatElapsed(directories.Timing.Total);
            DirectoryPairsList.ItemsSource = directories.DirectoryPairs.Take(25)
                .Select(pair => $"{pair.Leverage,6:N0}    {pair.First.Value}    ↔    {pair.Second.Value}")
                .ToArray();

            BranchPairCountText.Text = "suspended";
            BranchAnalysisElapsedText.Text = "—";
            BranchPairsList.ItemsSource = null;

            StatusText.Text = "Searching targeted branch counterparts...";
            var corpus = controller.Corpus
                ?? throw new InvalidOperationException("Corpus is unavailable.");
            var counterpartResult = counterpartAnalyzer.Analyze(
                corpus,
                branchStatistics.Branches,
                duplicates.DuplicateSets,
                directories.DirectoryPairs,
                controller.DuplicateSettlements,
                seedLimit: 25,
                counterpartLimit: 10);

            var report = new StringBuilder();
            report.AppendLine("Experimental run: comprehensive BranchPair generation suspended");
            report.AppendLine($"  corpus roots: {scan.Roots.Count:N0}");
            foreach (var root in scan.Roots)
                report.AppendLine($"    {root}");
            report.AppendLine($"  current portrait: {duplicates.Portrait.Files.Count:N0} files; {duplicates.Portrait.Files.Sum(file => file.Length):N0} bytes");
            report.AppendLine($"  duplicate sets: {duplicates.DuplicateSets.Count:N0}; duplicate files: {duplicates.DuplicateFileCount:N0}");
            report.AppendLine($"  analyzed directories: {directories.Directories.Count:N0}; DirectoryPairs: {directories.DirectoryPairs.Count:N0}; BranchPairs: not generated");
            report.AppendLine("  measured phase times:");
            report.AppendLine($"    scan total:              {FormatElapsed(scan.TotalElapsed)}");
            report.AppendLine($"    duplicate discovery:     {FormatElapsed(duplicates.Timing.Total)}");
            report.AppendLine($"    directory analysis:      {FormatElapsed(directories.Timing.Total)}");
            report.AppendLine($"    branch statistics:       {FormatElapsed(branchStatistics.Elapsed)}");
            report.AppendLine($"    targeted counterparts:   {FormatElapsed(counterpartResult.Elapsed)}");
            report.Append(BranchStatisticsFormatter.Format(branchStatistics));
            report.Append(BranchCounterpartFormatter.Format(counterpartResult));
            report.Append(FormatEarlySettlementSummary(candidates, accepted));
            report.Append(FormatConfigSummary(config));
            CasesReportText.Text = report.ToString();

            var evictionText = duplicates.Evictions.Count == 0
                ? string.Empty
                : $" {duplicates.Evictions.Count:N0} inaccessible file(s) removed from the portrait.";
            StatusText.Text = $"Experimental analysis complete: {counterpartResult.Seeds.Count:N0} branch seeds examined after {accepted.Count:N0} accepted distributed DuplicateSet(s)." + evictionText;
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

    private static string FormatConfigSummary(BerriesConfig config)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Berries.config");
        builder.AppendLine($"  path: {config.Path}");
        builder.AppendLine($"  ignore rules: {config.IgnorePatterns.Count:N0}");
        foreach (var pattern in config.IgnorePatterns)
            builder.AppendLine($"    {pattern}");
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
