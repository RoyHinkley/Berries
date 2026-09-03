using System.Text.Json;
using Avalonia.Interactivity;
using Berries.Core.Analysis;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private async void PivotDirectoryNamesakeMinHash_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session;
        if (session is null || currentProjection?.Kind == ProjectionKind.DirectoryNamesakeMinHash) return;

        var operation = BeginNavigation("Finding Namesake MinHash candidates...", true);
        try
        {
            var candidates = await Task.Run(
                () => DirectoryNamesakeMinHashAnalyzer.Analyze(
                    session,
                    fileSystem,
                    cancellationToken: operation.Token),
                operation.Token);
            operation.Token.ThrowIfCancellationRequested();

            var jsonPath = Path.Combine(AppContext.BaseDirectory, "namesake-minhash.json");
            var json = JsonSerializer.Serialize(
                candidates.Select((candidate, index) => new
                {
                    Index = index + 1,
                    Namesake = Path.GetFileName(candidate.Members[0].Path.Value),
                    candidate.MatchingBands,
                    candidate.TotalBands,
                    Bands = candidate.Bands.Select(band => band + 1),
                    MinimumDescendantNamesakeCount = candidate.Members.Min(member => member.DescendantNamesakeCount),
                    AverageDescendantNamesakeCount = candidate.Members.Average(member => member.DescendantNamesakeCount),
                    MaximumNamesakeDepth = candidate.Members.Max(member => member.MaxDescendantNamesakeDepth),
                    AverageNamesakeDepth = candidate.Members.Average(member => member.MaxDescendantNamesakeDepth),
                    Members = candidate.Members.Select(member => new
                    {
                        Path = member.Path.Value,
                        member.DescendantNamesakeCount,
                        member.DistinctDescendantNamesakeCount,
                        member.MaxDescendantNamesakeDepth
                    })
                }),
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonPath, json, operation.Token);

            var nodes = await Task.Run(() =>
                candidates.Select((candidate, index) =>
                {
                    operation.Token.ThrowIfCancellationRequested();

                    var namesake = Path.GetFileName(candidate.Members[0].Path.Value);
                    var minCount = candidate.Members.Min(member => member.DescendantNamesakeCount);
                    var averageCount = candidate.Members.Average(member => member.DescendantNamesakeCount);
                    var maxDepth = candidate.Members.Max(member => member.MaxDescendantNamesakeDepth);

                    var root = new ExplorerNode(
                        $"{index + 1}. {namesake} — {candidate.MatchingBands}/{candidate.TotalBands} bands — "
                        + $"{candidate.Members.Count:N0} directories — "
                        + $"Namesakes min {minCount:N0}, avg {averageCount:F1} — max depth {maxDepth:N0}",
                        []);

                    foreach (var member in candidate.Members)
                        root.Children.Add(new ExplorerNode(
                            $"{member.Path.Value} — {member.DescendantNamesakeCount:N0} descendant Namesake directories — "
                            + $"{member.DistinctDescendantNamesakeCount:N0} distinct descendant Namesake names — "
                            + $"depth {member.MaxDescendantNamesakeDepth:N0}",
                            [],
                            member.Path));

                    return root;
                }).ToArray(), operation.Token);

            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.DirectoryNamesakeMinHash, []);
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = $"Namesake MinHash — {candidates.Count:N0} candidates";
            ExplorerTree.ItemsSource = nodes;
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, $"Wrote {jsonPath}");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not build Namesake MinHash view: " + ex.Message);
        }
    }
}
