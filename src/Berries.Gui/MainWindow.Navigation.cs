using Avalonia.Controls;
using Avalonia.Controls.Selection;

namespace Berries.Gui;

public partial class MainWindow
{
    private void CancelRootsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (controller.Session is null)
            return;

        RootsPanel.IsVisible = false;
        ExplorerPanel.IsVisible = true;
        StatusText.Text = "Returned to the current session.";
    }

    private void ExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged is a UI capability check only. Do not expand a selected
        // directory node into all of its descendant FileInstances here: a large branch
        // can contain thousands of files, and doing that synchronously on every click
        // makes ordinary tree navigation appear to hang.
        var hasSelection = sender is TreeView tree && tree.SelectedItems is { Count: > 0 };

        InvertButton.IsEnabled = hasSelection;
        ExcludeButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void UpdateRootsCancelCapability() =>
        CancelRootsButton.IsEnabled = controller.Session is not null;
}
