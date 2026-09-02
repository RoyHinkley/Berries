using Berries.Core.Analysis;

namespace Berries.Core;

/// <summary>
/// Analyzer-independent candidate submitted for possible presentation to the user.
/// Candidates carry evidence and navigation data, but no cross-Suggestion rank.
/// </summary>
public abstract record SuggestionCandidate;

public sealed record BranchPairSuggestionCandidate(BranchPairSuggestion BranchPair) : SuggestionCandidate;

/// <summary>
/// A candidate after the Suggestion policy has assigned its comparable rank.
/// </summary>
public sealed record Suggestion(string Id, SuggestionCandidate Candidate, double Rank);

/// <summary>
/// Receives candidates from independent analyzers, applies the central Suggestion ranking
/// policy, maintains one ordered generation-scoped pool, and dispenses Suggestions cyclically.
/// Thread-safe so independent analyzers may submit concurrently.
/// </summary>
public sealed class SuggestionBox
{
    private readonly object gate = new();
    private readonly Dictionary<string, Suggestion> suggestions = new(StringComparer.Ordinal);
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private long generation = -1;
    private Suggestion? current;

    public void Reset(long newGeneration)
    {
        lock (gate)
        {
            generation = newGeneration;
            suggestions.Clear();
            seen.Clear();
            current = null;
        }
    }

    public bool Submit(long candidateGeneration, SuggestionCandidate candidate)
    {
        lock (gate)
        {
            if (candidateGeneration != generation)
                return false;

            var suggestion = CreateSuggestion(candidate);
            suggestions[suggestion.Id] = suggestion;
            return true;
        }
    }

    public Suggestion? PeekNext(long currentGeneration)
    {
        lock (gate)
        {
            if (currentGeneration != generation)
                return null;

            var ordered = OrderedSuggestions().ToArray();
            return ordered.FirstOrDefault(item => !seen.Contains(item.Id))
                ?? ordered.FirstOrDefault();
        }
    }

    public Suggestion? TakeNext(long currentGeneration)
    {
        lock (gate)
        {
            if (currentGeneration != generation)
                return null;

            var ordered = OrderedSuggestions().ToArray();
            var next = ordered.FirstOrDefault(item => !seen.Contains(item.Id));
            if (next is null)
            {
                seen.Clear();
                next = ordered.FirstOrDefault();
            }
            if (next is null)
                return null;

            seen.Add(next.Id);
            current = next;
            return next;
        }
    }

    public Suggestion? Current(long currentGeneration)
    {
        lock (gate)
            return currentGeneration == generation ? current : null;
    }

    private IEnumerable<Suggestion> OrderedSuggestions() =>
        suggestions.Values
            .OrderByDescending(item => item.Rank)
            .ThenByDescending(BranchPairSeedPriority)
            .ThenBy(BranchPairCandidateSeedRank)
            .ThenBy(item => item.Id, StringComparer.Ordinal);

    private static Suggestion CreateSuggestion(SuggestionCandidate candidate) => candidate switch
    {
        BranchPairSuggestionCandidate branchPair => new Suggestion(
            BranchPairId(branchPair.BranchPair),
            candidate,
            BranchPairRank(branchPair.BranchPair)),
        _ => throw new NotSupportedException($"No Suggestion ranking policy is defined for {candidate.GetType().Name}.")
    };

    // Current Branch Pair comparison policy. This deliberately lives here rather than in
    // BranchCounterpartAnalyzer: analyzers discover promising candidates; Suggestions decides
    // how candidates compare for presentation.
    private static double BranchPairRank(BranchPairSuggestion suggestion)
    {
        if (suggestion.Counterparts.Count == 0)
            return double.MinValue;
        var counterpart = suggestion.Counterparts[0];
        return counterpart.SharedGroupCount * counterpart.Jaccard;
    }

    private static double BranchPairSeedPriority(Suggestion suggestion) =>
        suggestion.Candidate is BranchPairSuggestionCandidate branchPair
            ? branchPair.BranchPair.Seed.ExcessConcentratedGroups
            : 0;

    private static int BranchPairCandidateSeedRank(Suggestion suggestion) =>
        suggestion.Candidate is BranchPairSuggestionCandidate branchPair
            ? branchPair.BranchPair.CandidateSeedRank
            : int.MaxValue;

    private static string BranchPairId(BranchPairSuggestion suggestion)
    {
        if (suggestion.Counterparts.Count == 0)
            throw new ArgumentException("A Branch Pair candidate must have a Counterpart.", nameof(suggestion));

        var first = suggestion.Seed.Branch.Path.Value;
        var second = suggestion.Counterparts[0].Branch.Path.Value;
        return StringComparer.Ordinal.Compare(first, second) <= 0
            ? $"BranchPair\u001f{first}\u001f{second}"
            : $"BranchPair\u001f{second}\u001f{first}";
    }
}
