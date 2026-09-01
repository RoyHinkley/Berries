using Avalonia.Interactivity;
using Berries.Core.Analysis;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

public partial class MainWindow
{
    private readonly HashSet<SuggestionKey> seenSuggestions = [];
    private BranchPairSuggestion? currentSuggestion;
    private long suggestionGeneration = -1;

    private void EnsureSuggestionGeneration()
    {
        var generation = controller.PortraitGeneration;
        if (suggestionGeneration == generation) return;

        suggestionGeneration = generation;
        seenSuggestions.Clear();
        currentSuggestion = null;
        suggestionIndex = -1;
    }

    private BranchPairSuggestion? HighestRankedUnseenSuggestion()
    {
        EnsureSuggestionGeneration();
        var suggestions = controller.Suggestions?.Suggestions;
        if (suggestions is null || suggestions.Count == 0) return null;

        return BranchCounterpartAnalyzer.RankSuggestions(suggestions)
            .FirstOrDefault(suggestion =>
                suggestion.Counterparts.Count > 0
                && !seenSuggestions.Contains(SuggestionKey.For(suggestion)));
    }

    private async void SuggestNextUnseen_Click(object? sender, RoutedEventArgs e)
    {
        var suggestion = HighestRankedUnseenSuggestion();
        if (suggestion is null) return;

        seenSuggestions.Add(SuggestionKey.For(suggestion));
        currentSuggestion = suggestion;
        suggestionIndex = 0; // Retains the existing "current Suggestion" capability contract.

        await ShowBranchPairAsync(suggestion);
        RebuildCurrentPairBreadcrumbs();
        SynchronizeVisibleSelection();
        UpdateSelectionSummary();
        SuggestButton.IsEnabled = HighestRankedUnseenSuggestion() is not null;
    }

    private async void PivotCurrentSuggestion_Click(object? sender, RoutedEventArgs e)
    {
        EnsureSuggestionGeneration();
        if (currentSuggestion is null) return;

        await ShowBranchPairAsync(currentSuggestion);
        RebuildCurrentPairBreadcrumbs();
        SynchronizeVisibleSelection();
        UpdateSelectionSummary();
    }

    private readonly record struct SuggestionKey(FileSystemPath First, FileSystemPath Second)
    {
        public static SuggestionKey For(BranchPairSuggestion suggestion) =>
            new(suggestion.Seed.Branch.Path, suggestion.Counterparts[0].Branch.Path);
    }
}
