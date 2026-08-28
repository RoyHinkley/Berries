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
        var selectedCount = SelectedFilesFromActiveProjection().Count;
        var hasSelection = selectedCount > 0;

        InvertButton.IsEnabled = hasSelection;
        ExcludeButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void UpdateRootsCancelCapability() =>
        CancelRootsButton.IsEnabled = controller.Session is not null;
}
