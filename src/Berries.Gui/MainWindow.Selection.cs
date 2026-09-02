using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Berries.Core.Domain;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private ExplorerNode? focusedNode;
    private bool synchronizingSelection;
    private ProjectionState? currentProjection;

    private void ExplorerNode_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (controller.Session is not { } session
            || e.GetCurrentPoint(sender as Visual).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed
            || e.Source is not Visual source)
            return;

        Visual? current = source;
        while (current is not null && current is not TreeViewItem)
        {
            // The disclosure button owns expansion/collapse and must not also toggle selection.
            if (current is ToggleButton) return;
            current = current.GetVisualParent();
        }

        if (current is not TreeViewItem { DataContext: ExplorerNode node }) return;

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

    private async void InvertSelectedCopies_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not { } session || session.Selection.IsEmpty) return;
        if (!await ConfirmOutsideSelectionAsync("Invert selected copies")) return;
        session.InvertSelectedCopies();
        SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
    }

    private async void InvertAllGroups_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is not { } session || !IsGroupsProjection()) return;
        if (!await ConfirmOutsideSelectionAsync("Invert all Groups")) return;
        session.Selection.Invert(RepresentedFiles());
        SynchronizeVisibleSelection(); UpdateSelectionSummary(); UpdateCapabilities();
    }

    private bool IsGroupsProjection() => currentProjection?.Kind == ProjectionKind.Groups;

    private IReadOnlyList<FileInstance> RepresentedFiles() => currentProjection?.RepresentedFiles ?? [];

    private async Task<bool> ConfirmOutsideSelectionAsync(string action)
    {
        if (controller.Session is not { } session) return false;

        var outside = session.Selection.CountOutside(RepresentedFiles());
        if (outside == 0) return true;

        var total = session.Selection.Count;
        var outsideText = outside == total
            ? $"All {outside:N0} selected file(s) are outside the current view."
            : $"{outside:N0} of {total:N0} selected file(s) are outside the current view.";

        var dialog = new Window
        {
            Title = "Selection outside current view",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var continueButton = new Button { Content = action, MinWidth = 90 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        continueButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"{outsideText}\n\nContinue with {action}?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { continueButton, cancelButton }
                }
            }
        };

        return await dialog.ShowDialog<bool>(this);
    }

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
                foreach (var node in EnumerateNodes(tree.ItemsSource)
                    .Where(node => node.Files.Count > 0 && node.Files.All(session.Selection.Contains)))
                    tree.SelectedItems.Add(node);
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
        var groups = session.SelectedGroupCount;
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
        if (currentProjection is not { IsPair: true, Primary: { } primary, Secondary: { } secondary } current)
            return;

        var firstPath = side == PairSide.Left ? path : primary;
        var secondPath = side == PairSide.Right ? path : secondary;
        var firstFiles = side == PairSide.Left ? DistinctFilesFast(files) : current.PrimaryFiles ?? [];
        var secondFiles = side == PairSide.Right ? DistinctFilesFast(files) : current.SecondaryFiles ?? [];
        SetPairProjectionState(current.Kind, firstPath, firstFiles, secondPath, secondFiles);
    }
}
