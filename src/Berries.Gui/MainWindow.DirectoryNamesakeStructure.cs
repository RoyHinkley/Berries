using Avalonia.Interactivity;
using Berries.Core.Analysis;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private async void PivotDirectoryNamesakeStructure_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session;
        if (session is null || currentProjection?.Kind == ProjectionKind.DirectoryNamesakeStructure) return;

        var operation = BeginNavigation("Finding Namesake structure...", true);
        try
        {
            var candidates = await Task.Run(
                () => DirectoryNamesakeStructureAnalyzer.Analyze(
                    session,
                    fileSystem,
                    cancellationToken: operation.Token),
                operation.Token);
            operation.Token.ThrowIfCancellationRequested();

            var nodes = await Task.Run(() =>
                candidates.Select((candidate, index) =>
                {
                    operation.Token.ThrowIfCancellationRequested();
                    var root = new ExplorerNode(
                        $"{index + 1}. {candidate.Branches.Count:N0} branches — "
                        + $"{candidate.SharedNamesakes.Count:N0} shared Namesakes — score {candidate.Score:F1}",
                        []);

                    var branches = new ExplorerNode("Branches", []);
                    foreach (var branch in candidate.Branches)
                        branches.Children.Add(new ExplorerNode(branch.Value, [], branch));
                    root.Children.Add(branches);

                    var evidence = new ExplorerNode("Shared Directory Namesakes", []);
                    foreach (var namesake in candidate.SharedNamesakes)
                        evidence.Children.Add(new ExplorerNode(
                            $"{namesake.Name} — {namesake.CorpusDirectoryCount:N0} directories in Corpus",
                            []));
                    root.Children.Add(evidence);

                    return root;
                }).ToArray(), operation.Token);

            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.DirectoryNamesakeStructure, []);
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = $"Namesake Structure — {candidates.Count:N0} candidates";
            ExplorerTree.ItemsSource = nodes;
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, ProjectionTitle.Text ?? "Namesake Structure");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not build Namesake Structure view: " + ex.Message);
        }
    }
}
