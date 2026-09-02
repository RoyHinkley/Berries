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

            var nodes = await Task.Run(() =>
                namesakes.Select(namesake =>
                {
                    operation.Token.ThrowIfCancellationRequested();
                    var node = new ExplorerNode(
                        $"{namesake.Name} — {namesake.Directories.Count:N0} directories",
                        namesake.Files);
                    foreach (var directory in namesake.Directories)
                        node.Children.Add(new ExplorerNode(
                            directory.Path.Value,
                            directory.Files,
                            directory.Path));
                    return node;
                }).ToArray(), operation.Token);

            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(
                ProjectionKind.DirectoryNamesakes,
                namesakes.SelectMany(namesake => namesake.Files));
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = $"Directory Namesakes — {namesakes.Count:N0}";
            ExplorerTree.ItemsSource = nodes;
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
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
}
