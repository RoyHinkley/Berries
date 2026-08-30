using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Berries.Core.Domain;

namespace Berries.Gui;

public partial class MainWindow
{
    private ExplorerNode? focusedNode;
    private bool synchronizingSelection;
    private ProjectionState? currentProjection;

    private void ExplorerNode_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ExplorerNode node } || controller.Session is not { } session) return;
        focusedNode = node;
        session.Selection.Toggle(node.Files);
        SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
        e.Handled = true;
    }

    private void ClearSelectionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not { } session) return;
        session.Selection.Clear(); SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
    }

    private void InvertSelectedCopies_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not { } session || session.Selection.IsEmpty) return;
        session.Selection.InvertSelectedCopies(session.DuplicateSets);
        SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
    }

    private void InvertAllGroups_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not { } session || !IsGroupsProjection()) return;
        session.Selection.Invert(RepresentedFiles());
        SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
    }

    private bool IsGroupsProjection() => currentProjection?.Kind == ProjectionKind.Groups;

    private IReadOnlyList<FileInstance> RepresentedFiles() => currentProjection?.RepresentedFiles ?? [];

    private IEnumerable<TreeView> ActiveTrees()
    {
        if (currentProjection?.IsPair == true) { yield return LeftTree; yield return RightTree; }
        else yield return ExplorerTree;
    }

    private void SynchronizeVisibleSelection()
    {
        if (controller.Session is not { } session) return;
        synchronizingSelection = true;
        try
        {
            foreach (var tree in ActiveTrees())
            {
                if (tree.SelectedItems is null) continue;
                tree.SelectedItems.Clear();
                foreach (var leaf in EnumerateNodes(tree.ItemsSource)
                    .Where(node => node.Children.Count == 0 && node.Files.Count == 1 && session.Selection.Contains(node.Files[0])))
                    tree.SelectedItems.Add(leaf);
            }
        }
        finally { synchronizingSelection = false; }
    }

    private void UpdateSelectionSummary()
    {
        var session = controller.Session;
        if (session is null || session.Selection.IsEmpty)
        {
            SelectionText.Text = "Selected: none"; SetSelectionCapabilities(false); return;
        }

        var selection = session.Selection;
        var groups = selection.CountGroups(session.DuplicateSets);
        var outside = selection.CountOutside(RepresentedFiles());
        SelectionText.Text = $"Selected: {selection.Count:N0} files · {groups:N0} groups · {outside:N0} outside view";
        SetSelectionCapabilities(true);
    }

    private void SetSelectionCapabilities(bool hasSelection)
    {
        ClearSelectionButton.IsEnabled = hasSelection;
        InvertButton.IsEnabled = hasSelection;
        InvertSelectedCopiesMenu.IsEnabled = hasSelection;
        InvertAllGroupsMenu.IsEnabled = hasSelection && IsGroupsProjection();
        ExcludeButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private IReadOnlyList<FileInstance> SemanticSelection() => controller.Session?.Selection.Files ?? [];

    private IReadOnlyList<FileInstance> SemanticSelectionInScope(Berries.FileSystem.Abstractions.FileSystemPath scope, bool descendants) =>
        controller.Session is { } session ? Projections.SelectedFilesInContext(session, scope, descendants) : [];

    private void SetProjectionState(ProjectionKind kind, IEnumerable<FileInstance> representedFiles,
        Berries.FileSystem.Abstractions.FileSystemPath? primary = null,
        Berries.FileSystem.Abstractions.FileSystemPath? secondary = null)
    {
        var represented = DistinctFilesFast(representedFiles);
        if (kind is ProjectionKind.DirectoryPair or ProjectionKind.BranchPair && primary is not null && secondary is not null)
        {
            var descendants = kind == ProjectionKind.BranchPair;
            SetPairProjectionState(
                kind,
                primary.Value,
                Projections.FilesInContext(represented, primary.Value, descendants),
                secondary.Value,
                Projections.FilesInContext(represented, secondary.Value, descendants));
            return;
        }

        currentProjection = new ProjectionState(kind, represented, primary, secondary);
        focusedNode = null;
    }

    private void SetPairProjectionState(
        ProjectionKind kind,
        Berries.FileSystem.Abstractions.FileSystemPath primary,
        IReadOnlyList<FileInstance> primaryFiles,
        Berries.FileSystem.Abstractions.FileSystemPath secondary,
        IReadOnlyList<FileInstance> secondaryFiles)
    {
        var first = DistinctFilesFast(primaryFiles);
        var second = DistinctFilesFast(secondaryFiles);
        currentProjection = new ProjectionState(
            kind,
            DistinctFilesFast(first.Concat(second)),
            primary,
            secondary,
            first,
            second);
        focusedNode = null;
    }

    private void UpdatePairProjectionSide(
        PairSide side,
        Berries.FileSystem.Abstractions.FileSystemPath path,
        IReadOnlyList<FileInstance> files)
    {
        if (currentProjection is not { IsPair: true, Primary: { } primary, Secondary: { } secondary } projection)
            return;

        var firstPath = side == PairSide.Left ? path : primary;
        var secondPath = side == PairSide.Right ? path : secondary;
        var firstFiles = side == PairSide.Left ? DistinctFilesFast(files) : projection.PrimaryFiles ?? [];
        var secondFiles = side == PairSide.Right ? DistinctFilesFast(files) : projection.SecondaryFiles ?? [];
        SetPairProjectionState(projection.Kind, firstPath, firstFiles, secondPath, secondFiles);
    }
}
