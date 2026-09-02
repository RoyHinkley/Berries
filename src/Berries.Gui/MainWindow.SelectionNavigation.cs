using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Berries.Gui;

public partial class MainWindow
{
    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !ExplorerPanel.IsVisible || controller.Session is not { } session)
            return;

        session.Selection.Clear();
        SynchronizeVisibleSelection();
        UpdateSelectionSummary();
        UpdateCapabilities();
        e.Handled = true;
    }

    private void PivotButton_Click(object? sender, RoutedEventArgs e) =>
        UpdatePivotCapabilities();

    private void ExplorerNode_ContextRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ExplorerNode node } control)
            return;

        focusedNode = node;
        PivotButton_Click(PivotButton, e);
        PivotButton.Flyout?.ShowAt(control);
        e.Handled = true;
    }

    private async void PivotContentOrAll_Click(object? sender, RoutedEventArgs e)
    {
        if (controller.Session is null)
            return;

        if (!controller.Session.Selection.IsEmpty)
        {
            PivotSelectedContent_Click(sender, e);
            return;
        }

        BreadcrumbPanel.IsVisible = false;
        BreadcrumbPanel.Children.Clear();
        await ShowContentProjectionAsync();
    }
}
