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
        BeginProgress("Opening Branch...", true);
        try
        {
            var node = await BuildBranchExplorerNodeAsync(branch);
            PairExplorer.IsVisible = false; SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.Branch, node.Files, branch);
            ProjectionTitle.Text = "Branch"; BuildBreadcrumbs(branch); ExplorerTree.ItemsSource = new[] { node };
            EndProgress("Branch"); SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities(); UpdatePivotCapabilities();
        }
        catch (Exception ex) { EndProgress("Could not open Branch: " + ex.Message); }
    }
}
