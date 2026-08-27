using System.Text;
using Berries.Core.Analysis;

namespace Berries.Gui;

internal static class BranchCounterpartFormatter
{
    public static string Format(BranchCounterpartResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Experimental culled BranchPair shortlist");
        builder.AppendLine($"  selected pairs: {result.Seeds.Count:N0}; analysis time: {FormatElapsed(result.Elapsed)}");
        builder.AppendLine("  each round: top 10 eligible seeds by D * (1 - 1/C); counterparts ranked by shared Content * Jaccard; strongest pair wins");
        builder.AppendLine("  direct DP shared is the shared Content count for the exact two branch-root directories");
        builder.AppendLine("  after each pair, both selected branches and all descendants are excluded");

        for (var index = 0; index < result.Seeds.Count; index++)
        {
            var item = result.Seeds[index];
            if (item.Counterparts.Count == 0)
                continue;

            builder.AppendLine();
            builder.AppendLine(
                $"  #{index + 1:N0} candidate-seed rank {item.CandidateSeedRank:N0}; " +
                $"seed score {item.Seed.ExcessConcentratedContent:N2}; " +
                $"D {item.Seed.Branch.DuplicateContentCount:N0}; C {item.Seed.Concentration:N2}  " +
                item.Seed.Branch.Path.Value);

            for (var candidateIndex = 0; candidateIndex < item.Counterparts.Count; candidateIndex++)
            {
                var counterpart = item.Counterparts[candidateIndex];
                builder.AppendLine(
                    $"      candidate {candidateIndex + 1:N0}: pair score {counterpart.Score,9:N2}; " +
                    $"shared {counterpart.SharedDuplicateContentCount,6:N0}; " +
                    $"coverage {counterpart.SeedCoverage,6:P1}/{counterpart.CounterpartCoverage,6:P1}; " +
                    $"Jaccard {counterpart.Jaccard,6:P1}; " +
                    $"direct DP shared {counterpart.DirectDirectoryPairSharedContentCount,5:N0}  " +
                    counterpart.Branch.Path.Value);
            }
        }

        return builder.ToString();
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("N3") + " s"
            : elapsed.TotalMilliseconds.ToString("N1") + " ms";
}
