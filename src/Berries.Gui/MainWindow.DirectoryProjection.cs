using Berries.Core.Queries;
using Berries.FileSystem.Abstractions;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private ProjectionService? projectionService;
    private ProjectionService Projections => projectionService ??= new ProjectionService(new PortraitQueries(fileSystem));

    private async Task<ExplorerNode> BuildDirectoryExplorerNodeAsync(
        FileSystemPath directory,
        CancellationToken cancellationToken = default)
    {
        var session = controller.Session ?? throw new InvalidOperationException("No session.");
        var projection = await Projections.DirectoryAsync(session, directory, cancellationToken: cancellationToken);
        return BuildDirectoryExplorerNode(projection);
    }

    private static ExplorerNode BuildDirectoryExplorerNode(DirectoryProjection projection)
    {
        var files = projection.Files.Select(item => item.File).ToArray();
        var root = new ExplorerNode(projection.Directory.Value, files);
        foreach (var item in projection.Files)
            root.Children.Add(new ExplorerNode(item.Label, [item.File]));
        return root;
    }

    private async Task ShowDirectoryProjectionAsync(FileSystemPath directory)
    {
        if (controller.Session is null) return;
        BeginProgress("Opening Directory...", true);
        try
        {
            var node = await BuildDirectoryExplorerNodeAsync(directory);
            currentScope = directory;
            scopeIncludesDescendants = false;
            scopeProjectionTitle = "Directory";
            leftScope = null;
            rightScope = null;
            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            ProjectionTitle.Text = "Directory";
            BuildBreadcrumbs(directory);
            ExplorerTree.ItemsSource = new[] { node };
            EndProgress("Directory");
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
        }
        catch (Exception ex)
        {
            EndProgress("Could not open Directory: " + ex.Message);
        }
    }

    private async Task ShowDirectoryPairProjectionAsync(Berries.Core.Analysis.DirectoryPair pair)
    {
        if (controller.Session is null) return;
        BeginProgress("Opening Directory Pair...", true);
        try
        {
            var leftTask = BuildDirectoryExplorerNodeAsync(pair.First);
            var rightTask = BuildDirectoryExplorerNodeAsync(pair.Second);
            await Task.WhenAll(leftTask, rightTask);

            currentScope = null;
            leftScope = pair.First;
            rightScope = pair.Second;
            PairExplorer.IsVisible = true;
            SingleExplorer.IsVisible = false;
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = $"Directory Pair — {pair.SharedContentCount:N0} shared Groups";
            BuildPairBreadcrumbs(pair.First, LeftScopeBreadcrumbs, false, "Directory", PairSide.Left);
            BuildPairBreadcrumbs(pair.Second, RightScopeBreadcrumbs, false, "Directory", PairSide.Right);
            LeftTree.ItemsSource = new[] { await leftTask };
            RightTree.ItemsSource = new[] { await rightTask };
            EndProgress($"Directory Pair — {pair.SharedContentCount:N0} shared Groups.");
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
        }
        catch (Exception ex)
        {
            EndProgress("Could not open Directory Pair: " + ex.Message);
        }
    }

    private async Task NavigateDirectoryPairBreadcrumbAsync(BreadcrumbTarget target)
    {
        var session = controller.Session;
        if (session is null || leftScope is null || rightScope is null || target.Side is null) return;

        var side = target.Side.Value;
        var first = side == PairSide.Left ? target.Path : leftScope.Value;
        var second = side == PairSide.Right ? target.Path : rightScope.Value;
        BeginProgress("Opening Directory...", true);
        try
        {
            var nodeTask = BuildDirectoryExplorerNodeAsync(target.Path);
            var sharedTask = Task.Run(() => CountSharedContents(session, first, second, includeDescendants: false));
            await Task.WhenAll(nodeTask, sharedTask);

            if (side == PairSide.Left)
            {
                leftScope = target.Path;
                LeftTree.ItemsSource = new[] { await nodeTask };
                BuildPairBreadcrumbs(target.Path, LeftScopeBreadcrumbs, false, "Directory", PairSide.Left);
            }
            else
            {
                rightScope = target.Path;
                RightTree.ItemsSource = new[] { await nodeTask };
                BuildPairBreadcrumbs(target.Path, RightScopeBreadcrumbs, false, "Directory", PairSide.Right);
            }

            var shared = await sharedTask;
            ProjectionTitle.Text = $"Directory Pair — {shared:N0} shared Groups";
            EndProgress($"Directory Pair — {shared:N0} shared Groups.");
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
        }
        catch (Exception ex)
        {
            EndProgress("Could not open Directory: " + ex.Message);
        }
    }
}
