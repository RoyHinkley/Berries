using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private readonly HashSet<string> selectedDirectoryNamesakeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemPath> selectedDirectoryNamesakeOccurrences = [];
    private readonly Dictionary<ExplorerNode, DirectoryNamesakeSelectionTarget> directoryNamesakeTargets = [];
    private IReadOnlyList<DirectoryNamesakeProjection> currentDirectoryNamesakes = [];
    private BerriesSession? directoryNamesakeSelectionSession;
    private IReadOnlyList<FileSystemPath> currentDirectoryProjectionScopes = [];

    private bool IsDirectoryNamesakesProjection() =>
        currentProjection?.Kind == ProjectionKind.DirectoryNamesakes;

    private void RegisterDirectoryNamesakeNodes(
        IReadOnlyList<DirectoryNamesakeProjection> namesakes,
        IReadOnlyList<ExplorerNode> nodes)
    {
        if (!ReferenceEquals(directoryNamesakeSelectionSession, controller.Session))
        {
            selectedDirectoryNamesakeNames.Clear();
            selectedDirectoryNamesakeOccurrences.Clear();
            directoryNamesakeSelectionSession = controller.Session;
        }

        currentDirectoryNamesakes = namesakes;
        directoryNamesakeTargets.Clear();

        for (var i = 0; i < namesakes.Count; i++)
        {
            var namesake = namesakes[i];
            var node = nodes[i];
            directoryNamesakeTargets[node] = new DirectoryNamesakeSelectionTarget(namesake.Name, null);

            for (var j = 0; j < namesake.Directories.Count; j++)
                directoryNamesakeTargets[node.Children[j]] = new DirectoryNamesakeSelectionTarget(
                    namesake.Name,
                    namesake.Directories[j].Path);
        }

        selectedDirectoryNamesakeNames.RemoveWhere(name =>
            !namesakes.Any(namesake => namesake.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        selectedDirectoryNamesakeOccurrences.RemoveAll(path =>
            !namesakes.Any(namesake => namesake.Directories.Any(directory => fileSystem.PathsEqual(directory.Path, path))));
    }

    private bool ToggleDirectoryNamesakeSelection(ExplorerNode node)
    {
        if (!directoryNamesakeTargets.TryGetValue(node, out var target))
            return false;

        if (target.Directory is null)
        {
            if (!selectedDirectoryNamesakeNames.Add(target.Name))
                selectedDirectoryNamesakeNames.Remove(target.Name);
        }
        else
        {
            ToggleDirectoryOccurrence(target.Directory.Value);
        }

        return true;
    }

    private void ToggleDirectoryOccurrence(FileSystemPath path)
    {
        var index = selectedDirectoryNamesakeOccurrences.FindIndex(selected => fileSystem.PathsEqual(selected, path));
        if (index >= 0)
            selectedDirectoryNamesakeOccurrences.RemoveAt(index);
        else
            selectedDirectoryNamesakeOccurrences.Add(path);
    }

    private bool IsDirectoryOccurrenceSelected(FileSystemPath path) =>
        selectedDirectoryNamesakeOccurrences.Any(selected => fileSystem.PathsEqual(selected, path));

    private bool HasDirectoryNamesakeSelection =>
        selectedDirectoryNamesakeNames.Count > 0 || selectedDirectoryNamesakeOccurrences.Count > 0;

    private void ClearDirectoryNamesakeSelection()
    {
        selectedDirectoryNamesakeNames.Clear();
        selectedDirectoryNamesakeOccurrences.Clear();
    }

    private void SynchronizeDirectoryNamesakeSelection()
    {
        synchronizingSelection = true;
        try
        {
            if (ExplorerTree.SelectedItems is null)
                return;

            ExplorerTree.SelectedItems.Clear();
            foreach (var (node, target) in directoryNamesakeTargets)
            {
                var selected = target.Directory is null
                    ? selectedDirectoryNamesakeNames.Contains(target.Name)
                    : IsDirectoryOccurrenceSelected(target.Directory.Value);
                if (selected)
                    ExplorerTree.SelectedItems.Add(node);
            }
        }
        finally
        {
            synchronizingSelection = false;
        }
    }

    private void UpdateDirectoryNamesakeSelectionSummary()
    {
        var namesakeCount = selectedDirectoryNamesakeNames.Count;
        var occurrenceCount = selectedDirectoryNamesakeOccurrences.Count;
        var hasSelection = namesakeCount > 0 || occurrenceCount > 0;

        if (!hasSelection)
        {
            SelectionText.Text = "Selected: none";
        }
        else if (occurrenceCount > 0)
        {
            SelectionText.Text = namesakeCount == 0
                ? $"Selected: {occurrenceCount:N0} director{(occurrenceCount == 1 ? "y" : "ies")}"
                : $"Selected: {occurrenceCount:N0} director{(occurrenceCount == 1 ? "y" : "ies")} · {namesakeCount:N0} Namesake{(namesakeCount == 1 ? string.Empty : "s")} ignored";
        }
        else
        {
            var effectiveCount = EffectiveDirectoryNamesakeSelection().Count;
            SelectionText.Text = $"Selected: {namesakeCount:N0} Namesake{(namesakeCount == 1 ? string.Empty : "s")} · {effectiveCount:N0} directories";
        }

        ClearSelectionButton.IsEnabled = hasSelection;
        InvertButton.IsEnabled = hasSelection;
        InvertSelectedCopiesMenu.IsEnabled = hasSelection;
        InvertSelectedCopiesMenu.Header = "Invert Directory Selection";
        InvertAllGroupsMenu.IsVisible = false;
        ExcludeButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = false;
        MoveRightButton.IsEnabled = false;
        MoveLeftButton.IsEnabled = false;
    }

    private void InvertDirectoryNamesakeSelection()
    {
        if (selectedDirectoryNamesakeOccurrences.Count > 0)
        {
            var affectedNamesakes = currentDirectoryNamesakes
                .Where(namesake => namesake.Directories.Any(directory => IsDirectoryOccurrenceSelected(directory.Path)))
                .ToArray();

            foreach (var namesake in affectedNamesakes)
                foreach (var directory in namesake.Directories)
                    ToggleDirectoryOccurrence(directory.Path);
            return;
        }

        if (selectedDirectoryNamesakeNames.Count == 0)
            return;

        foreach (var namesake in currentDirectoryNamesakes)
        {
            if (!selectedDirectoryNamesakeNames.Add(namesake.Name))
                selectedDirectoryNamesakeNames.Remove(namesake.Name);
        }
    }

    private IReadOnlyList<FileSystemPath> EffectiveDirectoryNamesakeSelection()
    {
        if (selectedDirectoryNamesakeOccurrences.Count > 0)
            return selectedDirectoryNamesakeOccurrences.ToArray();

        var result = new List<FileSystemPath>();
        foreach (var namesake in currentDirectoryNamesakes)
        {
            if (!selectedDirectoryNamesakeNames.Contains(namesake.Name))
                continue;

            foreach (var directory in namesake.Directories)
            {
                if (!result.Any(existing => fileSystem.PathsEqual(existing, directory.Path)))
                    result.Add(directory.Path);
            }
        }
        return result;
    }

    private void UpdateDirectoryNamesakePivotCapabilities()
    {
        var hasSession = controller.Session is not null;
        var directories = EffectiveDirectoryNamesakeSelection();

        PivotCorpusRootsMenu.IsEnabled = hasSession;
        PivotContentMenu.IsEnabled = hasSession;
        PivotDirectoryMenu.IsEnabled = hasSession && directories.Count > 0;

        // These pivots currently derive one seed and search for a best counterpart.
        // That is not the same operation as applying the Namesake directory selection.
        PivotBranchMenu.IsEnabled = false;
        PivotBestDirectoryPairMenu.IsEnabled = false;
        PivotBestBranchPairMenu.IsEnabled = false;
        PivotBranchPairMenu.IsEnabled = false;
    }

    private async void PivotDirectoryOrNamesakes_Click(object? sender, RoutedEventArgs e)
    {
        if (!IsDirectoryNamesakesProjection())
        {
            PivotDirectory_Click(sender, e);
            return;
        }

        var directories = EffectiveDirectoryNamesakeSelection();
        if (directories.Count > 0)
            await ShowDirectoryProjectionAsync(directories);
    }

    private async Task ShowDirectoryProjectionAsync(IReadOnlyList<FileSystemPath> directories)
    {
        var session = controller.Session;
        if (session is null || directories.Count == 0)
            return;

        if (directories.Count == 1)
        {
            currentDirectoryProjectionScopes = directories.ToArray();
            await ShowDirectoryProjectionAsync(directories[0]);
            return;
        }

        var operation = BeginNavigation("Opening Directories...", true);
        try
        {
            var projections = await DirectoryProjectionBatch.BuildAsync(
                session,
                directories,
                new Progress<OperationProgress>(progress => ShowNavigationProgress(operation, progress)),
                operation.Token);
            var nodes = projections.Select(BuildDirectoryExplorerNode).ToArray();
            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(operation.Token);

            PairExplorer.IsVisible = false;
            SingleExplorer.IsVisible = true;
            currentDirectoryProjectionScopes = directories.ToArray();
            SetProjectionState(ProjectionKind.Directory, nodes.SelectMany(node => node.Files));
            ProjectionTitle.Text = $"Directories — {directories.Count:N0}";
            BreadcrumbPanel.IsVisible = false;
            BreadcrumbPanel.Children.Clear();
            ExplorerTree.ItemsSource = nodes;
            SynchronizeVisibleSelection();
            UpdateSelectionSummary();
            UpdateCapabilities();
            UpdatePivotCapabilities();
            CompleteNavigation(operation, ProjectionTitle.Text ?? "Directories");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested || !IsCurrentNavigation(operation))
        {
            RetireNavigation(operation);
        }
        catch (Exception ex)
        {
            CompleteNavigation(operation, "Could not open Directories: " + ex.Message);
        }
    }

    private async Task RefreshMultipleDirectoryProjectionAsync()
    {
        var session = controller.Session;
        if (session is null || currentDirectoryProjectionScopes.Count <= 1)
            return;

        var directories = currentDirectoryProjectionScopes.ToArray();
        var projections = await DirectoryProjectionBatch.BuildAsync(session, directories);
        var nodes = projections.Select(BuildDirectoryExplorerNode).ToArray();
        ExplorerTree.ItemsSource = nodes;
        SetProjectionState(ProjectionKind.Directory, nodes.SelectMany(node => node.Files));
        currentDirectoryProjectionScopes = directories;
        ProjectionTitle.Text = $"Directories — {directories.Length:N0}";
        BreadcrumbPanel.IsVisible = false;
        BreadcrumbPanel.Children.Clear();
    }

    private async Task ExcludeDirectoryNamesakeSelectionAsync()
    {
        var session = controller.Session;
        var directories = EffectiveDirectoryNamesakeSelection();
        if (session is null || directories.Count == 0 || portraitCommandBusy)
            return;

        var choice = await ShowDirectoryNamesakeExcludeDialogAsync();
        if (choice == DirectoryNamesakeExcludeChoice.Cancel)
            return;

        var files = DistinctFilesFast(directories.SelectMany(directory =>
            Projections.FilesInContext(session.WorkingPortrait.Files, directory, true)));

        if (choice == DirectoryNamesakeExcludeChoice.Permanent)
        {
            var patterns = selectedDirectoryNamesakeOccurrences.Count > 0
                ? directories.Select(DirectoryPathExcludePattern)
                : selectedDirectoryNamesakeNames.Select(name => $"/{name}/");
            BerriesConfig.AddExcludePatterns(
                Path.Combine(AppContext.BaseDirectory, "Berries.config"),
                patterns);
        }

        ClearDirectoryNamesakeSelection();

        if (files.Count == 0)
        {
            SynchronizeDirectoryNamesakeSelection();
            UpdateDirectoryNamesakeSelectionSummary();
            StatusText.Text = choice == DirectoryNamesakeExcludeChoice.Permanent
                ? "Permanent exclusion added to config; no grouped files were present in the selected directories."
                : "No grouped files were present in the selected directories.";
            return;
        }

        await RunPortraitCommandAsync(
            $"Excluding files beneath {directories.Count:N0} director{(directories.Count == 1 ? "y" : "ies")}...",
            choice == DirectoryNamesakeExcludeChoice.Permanent
                ? $"Excluded files beneath {directories.Count:N0} director{(directories.Count == 1 ? "y" : "ies")} and added the exclusion to config."
                : $"Excluded files beneath {directories.Count:N0} director{(directories.Count == 1 ? "y" : "ies")} from this session.",
            () => controller.ExcludeAsync(files));
    }

    private async Task<DirectoryNamesakeExcludeChoice> ShowDirectoryNamesakeExcludeDialogAsync()
    {
        var sessionButton = new Button { Content = "This session only", MinWidth = 130 };
        var permanentButton = new Button { Content = "Add permanent exclusion to config", MinWidth = 220 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };

        var dialog = new Window
        {
            Title = "Exclude selected directories",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        sessionButton.Click += (_, _) => dialog.Close(DirectoryNamesakeExcludeChoice.Session);
        permanentButton.Click += (_, _) => dialog.Close(DirectoryNamesakeExcludeChoice.Permanent);
        cancelButton.Click += (_, _) => dialog.Close(DirectoryNamesakeExcludeChoice.Cancel);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "Exclude the selected directories from the current session, or also save equivalent directory exclusion rule(s) in Berries.config?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, sessionButton, permanentButton }
                }
            }
        };

        return await dialog.ShowDialog<DirectoryNamesakeExcludeChoice>(this);
    }

    private static string DirectoryPathExcludePattern(FileSystemPath path)
    {
        var normalized = path.Value.Replace('\\', '/').Trim('/');
        return $"/{normalized}/";
    }
}

internal sealed record DirectoryNamesakeSelectionTarget(string Name, FileSystemPath? Directory);

internal enum DirectoryNamesakeExcludeChoice
{
    Cancel,
    Session,
    Permanent
}
