using Avalonia.Interactivity;
using Berries.Core;

namespace Berries.Gui;

public partial class MainWindow
{
    private Suggestion? HighestRankedUnseenSuggestion() => controller.PeekSuggestion();

    private async void SuggestNextUnseen_Click(object? sender, RoutedEventArgs e)
    {
        var suggestion = controller.TakeSuggestion();
        if (suggestion is null) return;

        suggestionIndex = 0; // Retains the existing "current Suggestion" capability contract.
        await ShowSuggestionAsync(suggestion);
        SuggestButton.IsEnabled = HighestRankedUnseenSuggestion() is not null;
        PivotBranchPairMenu.IsEnabled = controller.CurrentSuggestion?.Candidate is BranchPairSuggestionCandidate;
    }

    private async void PivotCurrentSuggestion_Click(object? sender, RoutedEventArgs e)
    {
        var suggestion = controller.CurrentSuggestion;
        if (suggestion is null) return;
        await ShowSuggestionAsync(suggestion);
    }

    private async Task ShowSuggestionAsync(Suggestion suggestion)
    {
        switch (suggestion.Candidate)
        {
            case BranchPairSuggestionCandidate branchPair:
                await ShowBranchPairAsync(branchPair.BranchPair);
                RebuildCurrentPairBreadcrumbs();
                SynchronizeVisibleSelection();
                UpdateSelectionSummary();
                break;

            default:
                StatusText.Text = $"No navigator is defined for {suggestion.Candidate.GetType().Name}.";
                break;
        }
    }
}
