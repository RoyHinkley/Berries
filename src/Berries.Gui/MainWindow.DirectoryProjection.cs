using Berries.Core.Queries;
using Berries.FileSystem.Abstractions;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private ProjectionService? projectionService;
    private ProjectionService Projections => projectionService ??= new ProjectionService(new PortraitQueries(fileSystem));

    private async Task<ExplorerNode> BuildDirectoryExplorerNodeAsync(FileSystemPath directory, CancellationToken cancellationToken = default)
    {
        var session = controller.Session ?? throw new InvalidOperationException("No session.");
        var projection = await Projections.DirectoryAsync(session, directory, cancellationToken: cancellationToken);
        return BuildDirectoryExplorerNode(projection);
    }

    private static ExplorerNode BuildDirectoryExplorerNode(DirectoryProjection projection)
    {
        var files = projection.Files.Select(item => item.File).ToArray();
        var root = new ExplorerNode(projection.Directory.Value, files, projection.Directory);
        foreach (var item in projection.Files)
            root.Children.Add(new ExplorerNode(item.Label, [item.File], item.File.ParentDirectory));
        return root;
    }

    private async Task ShowDirectoryProjectionAsync(FileSystemPath directory)
    {
        if (controller.Session is null) return;
        var operation = BeginNavigation("Opening Directory...", true);
        try
        {
            var node = await BuildDirectoryExplorerNodeAsync(directory, operation.Token);
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            SetProjectionState(ProjectionKind.Directory, node.Files, directory);
            ProjectionTitle.Text = "Directory";
            BuildBreadcrumbs(directory);
            ExplorerTree.ItemsSource = new[] { node };
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, "Directory");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not open Directory: " + ex.Message);
        }
    }

    private async Task ShowDirectoryPairProjectionAsync(Berries.Core.Analysis.DirectoryPair pair)
    {
        if (controller.Session is null) return;
        var operation = BeginNavigation("Opening Directory Pair...", true);
        try
        {
            var leftTask = BuildDirectoryExplorerNodeAsync(pair.First, operation.Token);
            var rightTask = BuildDirectoryExplorerNodeAsync(pair.Second, operation.Token);
            await Task.WhenAll(leftTask, rightTask);
            var left = await leftTask;
            var right = await rightTask;
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = true;
            SingleExplorer.IsVisible = false;
            SetPairProjectionState(ProjectionKind.DirectoryPair, pair.First, left.Files, pair.Second, right.Files);
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ProjectionTitle.Text = $"Directory Pair — {pair.SharedGroupCount:N0} shared Groups";
            BuildPairBreadcrumbs(pair.First, LeftScopeBreadcrumbs, false, "Directory", PairSide.Left);
            BuildPairBreadcrumbs(pair.Second, RightScopeBreadcrumbs, false, "Directory", PairSide.Right);
            LeftTree.ItemsSource = new[] { left };
            RightTree.ItemsSource = new[] { right };
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, $"Directory Pair — {pair.SharedGroupCount:N0} shared Groups.");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not open Directory Pair: " + ex.Message);
        }
    }

    private async Task NavigateDirectoryPairBreadcrumbAsync(BreadcrumbTarget target)
    {
        var session = controller.Session;
        if (session is null
            || currentProjection is not { Primary: { } primary, Secondary: { } secondary }
            || target.Side is null)
            return;

        var side = target.Side.Value;
        var first = side == PairSide.Left ? target.Path : primary;
        var second = side == PairSide.Right ? target.Path : secondary;
        var operation = BeginNavigation("Opening Directory...", true);
        try
        {
            var nodeTask = BuildDirectoryExplorerNodeAsync(target.Path, operation.Token);
            var sharedTask = Projections.SharedGroupCountAsync(
                session,
                first,
                second,
                includeDescendants: false,
                cancellationToken: operation.Token);
            await Task.WhenAll(nodeTask, sharedTask);
            var node = await nodeTask;
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            if (side == PairSide.Left)
            {
                LeftTree.ItemsSource = new[] { node };
                BuildPairBreadcrumbs(target.Path, LeftScopeBreadcrumbs, false, "Directory", PairSide.Left);
            }
            else
            {
                RightTree.ItemsSource = new[] { node };
                BuildPairBreadcrumbs(target.Path, RightScopeBreadcrumbs, false, "Directory", PairSide.Right);
            }

            UpdatePairProjectionSide(side, target.Path, node.Files);
            var shared = await sharedTask;
            ProjectionTitle.Text = $"Directory Pair — {shared:N0} shared Groups";
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, $"Directory Pair — {shared:N0} shared Groups.");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not open Directory: " + ex.Message);
        }
    }
}
