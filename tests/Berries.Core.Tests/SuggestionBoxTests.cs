using Berries.Core.Analysis;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class SuggestionBoxTests
{
    [Fact]
    public void TakeNext_RanksBranchPairsInsideSuggestionBox()
    {
        var box = new SuggestionBox();
        box.Reset(7);

        var analyzerFavored = BranchPair(
            @"X:\SeedA",
            @"X:\OtherA",
            sharedGroups: 2,
            jaccard: 0.5,
            analyzerScore: 999);
        var suggestionFavored = BranchPair(
            @"X:\SeedB",
            @"X:\OtherB",
            sharedGroups: 10,
            jaccard: 0.5,
            analyzerScore: 0);

        Assert.True(box.Submit(7, new BranchPairSuggestionCandidate(analyzerFavored)));
        Assert.True(box.Submit(7, new BranchPairSuggestionCandidate(suggestionFavored)));

        var first = Assert.IsType<BranchPairSuggestionCandidate>(box.TakeNext(7)!.Candidate);
        Assert.Same(suggestionFavored, first.BranchPair);

        var second = Assert.IsType<BranchPairSuggestionCandidate>(box.TakeNext(7)!.Candidate);
        Assert.Same(analyzerFavored, second.BranchPair);
        Assert.Null(box.TakeNext(7));
    }

    [Fact]
    public void Submit_RejectsObsoleteGeneration()
    {
        var box = new SuggestionBox();
        box.Reset(2);

        Assert.False(box.Submit(1, new BranchPairSuggestionCandidate(BranchPair(
            @"X:\Seed",
            @"X:\Other",
            3,
            1,
            3))));
        Assert.Null(box.PeekNext(2));
    }

    private static BranchPairSuggestion BranchPair(
        string seedPath,
        string counterpartPath,
        int sharedGroups,
        double jaccard,
        double analyzerScore)
    {
        var seedBranch = new BranchRecord(new FileSystemPath(seedPath), null, 10, 1, 10, 10, 1);
        var seed = new BranchPriorityMetric(seedBranch, 1, 1, 1, 10, 0, 5);
        var counterpartBranch = new BranchRecord(new FileSystemPath(counterpartPath), null, 10, 1, 10, 10, 1);
        var counterpart = new BranchCounterpart(
            counterpartBranch,
            sharedGroups,
            1,
            1,
            jaccard,
            analyzerScore);
        return new BranchPairSuggestion(seed, [counterpart], 1);
    }
}
