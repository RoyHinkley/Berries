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
            var analysis = await Task.Run(
                () => DirectoryNamesakeMinHashAnalyzer.Analyze(
                    session,
                    fileSystem,
                    cancellationToken: operation.Token),
                operation.Token);
            operation.Token.ThrowIfCancellationRequested();

            var jsonPath = Path.Combine(AppContext.BaseDirectory, "namesake-minhash.json");
            var json = JsonSerializer.Serialize(
                analysis.Candidates.Select((candidate, index) => new
                {
                    Index = index + 1,
                    candidate.Namesake,
                    candidate.TotalOccurrences,
                    candidate.IntrinsicFamilyCount,
                    candidate.IntrinsicSupportingOccurrenceCount,
                    candidate.ResidualFamilyCount,
                    candidate.ResidualSupportingOccurrenceCount,
                    ResidualSupportFraction = candidate.IntrinsicSupportingOccurrenceCount == 0
                        ? 0
                        : (double)candidate.ResidualSupportingOccurrenceCount / candidate.IntrinsicSupportingOccurrenceCount,
                    Families = candidate.Families.Select((family, familyIndex) => new
                    {
                        Index = familyIndex + 1,
                        family.MatchingBands,
                        family.TotalBands,
                        Bands = family.Bands.Select(band => band + 1),
                        MinimumDescendantNamesakeCount = family.Members.Min(member => member.DescendantNamesakeCount),
                        AverageDescendantNamesakeCount = family.Members.Average(member => member.DescendantNamesakeCount),
                        MaximumNamesakeDepth = family.Members.Max(member => member.MaxDescendantNamesakeDepth),
                        AverageNamesakeDepth = family.Members.Average(member => member.MaxDescendantNamesakeDepth),
                        Members = family.Members.Select(member => new
                        {
                            Path = member.Path.Value,
                            member.DescendantNamesakeCount,
                            member.DistinctDescendantNamesakeCount,
                            member.MaxDescendantNamesakeDepth
                        })
                    })
                }),
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonPath, json, operation.Token);

            const int displayLimit = 250;
            var displayedCandidates = analysis.Candidates.Take(displayLimit).ToArray();
            var nodes = await Task.Run(() =>
                displayedCandidates.Select((candidate, index) =>
                {
                    operation.Token.ThrowIfCancellationRequested();

                    var residualPercent = candidate.IntrinsicSupportingOccurrenceCount == 0
                        ? 0
                        : 100.0 * candidate.ResidualSupportingOccurrenceCount / candidate.IntrinsicSupportingOccurrenceCount;
                    var root = new ExplorerNode(
                        $"{index + 1}. {candidate.Namesake} — "
                        + $"{candidate.ResidualSupportingOccurrenceCount:N0}/{candidate.IntrinsicSupportingOccurrenceCount:N0} supporting occurrences ({residualPercent:F0}% residual) — "
                        + $"{candidate.ResidualFamilyCount:N0}/{candidate.IntrinsicFamilyCount:N0} families — "
                        + $"{candidate.TotalOccurrences:N0} total occurrences",
                        []);

                    foreach (var family in candidate.Families
                                 .OrderByDescending(item => item.MatchingBands)
                                 .ThenByDescending(item => item.Members.Min(member => member.DescendantNamesakeCount)))
                    {
                        var minCount = family.Members.Min(member => member.DescendantNamesakeCount);
                        var averageCount = family.Members.Average(member => member.DescendantNamesakeCount);
                        var maxDepth = family.Members.Max(member => member.MaxDescendantNamesakeDepth);
                        var familyNode = new ExplorerNode(
                            $"{family.MatchingBands}/{family.TotalBands} bands — {family.Members.Count:N0} directories — "
                            + $"Namesakes min {minCount:N0}, avg {averageCount:F1} — max depth {maxDepth:N0}",
                            []);

                        foreach (var member in family.Members)
                            familyNode.Children.Add(new ExplorerNode(
                                $"{member.Path.Value} — {member.DescendantNamesakeCount:N0} descendant Namesake directories — "
                                + $"{member.DistinctDescendantNamesakeCount:N0} distinct descendant Namesake names — "
                                + $"depth {member.MaxDescendantNamesakeDepth:N0}",
                                [],
                                member.Path));

                        root.Children.Add(familyNode);
                    }

                    return root;
                }).ToArray(), operation.Token);

            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.DirectoryNamesakeMinHash, []);
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = analysis.Candidates.Count > displayLimit
                ? $"Namesake MinHash — {displayedCandidates.Length:N0} of {analysis.Candidates.Count:N0} Namesake candidates"
                : $"Namesake MinHash — {analysis.Candidates.Count:N0} Namesake candidates";
            ExplorerTree.ItemsSource = nodes;
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(
                operation,
                $"Wrote {analysis.Candidates.Count:N0} greedily ranked Namesake candidates to {jsonPath}");
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
