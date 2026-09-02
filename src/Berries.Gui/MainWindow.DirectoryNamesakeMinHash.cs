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

        var operation = BeginNavigation("Finding Namesake MinHash buckets...", true);
        try
        {
            var buckets = await Task.Run(
                () => DirectoryNamesakeMinHashAnalyzer.Analyze(
                    session,
                    fileSystem,
                    cancellationToken: operation.Token),
                operation.Token);
            operation.Token.ThrowIfCancellationRequested();

            var nodes = await Task.Run(() =>
                buckets.Select((bucket, index) =>
                {
                    operation.Token.ThrowIfCancellationRequested();
                    var root = new ExplorerNode(
                        $"{index + 1}. Band {bucket.Band + 1} — {bucket.Members.Count:N0} directories — {bucket.BandHash:X16}",
                        []);

                    foreach (var member in bucket.Members)
                        root.Children.Add(new ExplorerNode(
                            $"{member.Path.Value} — {member.DescendantNamesakeCount:N0} descendant Namesakes",
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
            ProjectionTitle.Text = $"Namesake MinHash — {buckets.Count:N0} matching band buckets";
            ExplorerTree.ItemsSource = nodes;
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, ProjectionTitle.Text ?? "Namesake MinHash");
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
