using Avalonia.Interactivity;
using Berries.Core;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private async void PivotDirectoryNamesakes_Click(object? sender, RoutedEventArgs e)
    {
        var session = controller.Session;
        if (session is null || currentProjection?.Kind == ProjectionKind.DirectoryNamesakes) return;

        var operation = BeginNavigation("Building Directory Namesakes view...", true);
        try
        {
            var namesakes = await Task.Run(
                () => DirectoryNamesakeProjections.Build(session, fileSystem),
                operation.Token);
            operation.Token.ThrowIfCancellationRequested();

            var nodes = await Task.Run(
                () => BuildDirectoryNamesakeNodes(namesakes, operation.Token),
                operation.Token);

            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            ApplyDirectoryNamesakeProjection(namesakes, nodes);
            CompleteNavigation(operation, ProjectionTitle.Text ?? "Directory Namesakes");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not build Directory Namesakes view: " + ex.Message);
        }
    }

    private ExplorerNode[] BuildDirectoryNamesakeNodes(
        IReadOnlyList<DirectoryNamesakeProjection> namesakes,
        CancellationToken cancellationToken)
    {
        return namesakes.Select(namesake =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = new ExplorerNode(
                $"{namesake.Name} — {namesake.Directories.Count:N0} directories");
            foreach (var directory in namesake.Directories)
                node.Children.Add(new ExplorerNode(
                    directory.Path.Value,
                    semanticPath: directory.Path));
            return node;
        }).ToArray();
    }

    private void ApplyDirectoryNamesakeProjection(
        IReadOnlyList<DirectoryNamesakeProjection> namesakes,
        IReadOnlyList<ExplorerNode> nodes)
    {
        PairExplorer.IsVisible = false;
        SingleExplorer.IsVisible = true;
        SetProjectionState(ProjectionKind.DirectoryNamesakes, []);
        BreadcrumbPanel.IsVisible = false;
        BreadcrumbPanel.Children.Clear();
        ProjectionTitle.Text = $"Directory Namesakes — {namesakes.Count:N0}";
        ExplorerTree.ItemsSource = nodes;
        RegisterDirectoryNamesakeNodes(namesakes, nodes);
        SynchronizeVisibleSelection();
        UpdateSelectionSummary();
        UpdateCapabilities();
        UpdateDirectoryNamesakePivotCapabilities();
    }

    private async Task RefreshDirectoryNamesakesProjectionAsync()
    {
        var session = controller.Session;
        if (session is null) return;

        var namesakes = await Task.Run(() => DirectoryNamesakeProjections.Build(session, fileSystem));
        var nodes = await Task.Run(() => BuildDirectoryNamesakeNodes(namesakes, CancellationToken.None));
        ApplyDirectoryNamesakeProjection(namesakes, nodes);
    }
}
