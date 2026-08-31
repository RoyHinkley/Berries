using Berries.FileSystem.Abstractions;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private async Task<ExplorerNode> BuildBranchExplorerNodeAsync(FileSystemPath branch, CancellationToken cancellationToken = default)
    {
        var session = controller.Session ?? throw new InvalidOperationException("No session.");
        var projection = await Projections.BranchAsync(session, branch, cancellationToken: cancellationToken);
        return BuildBranchExplorerNode(projection.Root);
    }

    private static ExplorerNode BuildBranchExplorerNode(BranchProjectionNode projection)
    {
        var node = new ExplorerNode(projection.Label, projection.Files, projection.Directory);
        foreach (var child in projection.Children) node.Children.Add(BuildBranchExplorerNode(child));
        return node;
    }

    private async Task ShowBranchProjectionAsync(FileSystemPath branch)
    {
        if (controller.Session is null) return;
        var operation = BeginNavigation("Opening Branch...", true);
        try
        {
            var node = await BuildBranchExplorerNodeAsync(branch, operation.Token);
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.Branch, node.Files, branch);
            ProjectionTitle.Text = "Branch";
            BuildBreadcrumbs(branch);
            ExplorerTree.ItemsSource = new[] { node };
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, "Branch");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not open Branch: " + ex.Message);
        }
    }
}
