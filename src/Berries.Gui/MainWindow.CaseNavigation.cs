using Avalonia.Interactivity;

namespace Berries.Gui;

public partial class MainWindow
{
    private async void PreviousCaseButton_Click(object? sender, RoutedEventArgs e)
    {
        var suggestions = controller.Counterparts?.Seeds;
        if (suggestions is null || suggestions.Count == 0)
            return;

        suggestionIndex = suggestionIndex <= 0
            ? suggestions.Count - 1
            : suggestionIndex - 1;

        await ShowBranchPairAsync(suggestions[suggestionIndex]);
    }
}
