using System.Text;
using Berries.Core.Analysis;

namespace Berries.Gui;

internal static class BranchCounterpartFormatter
{
    public static string Format(BranchCounterpartResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Experimental targeted branch counterpart search");
        builder.AppendLine($"  seeds: {result.Seeds.Count:N0}; analysis time: {FormatElapsed(result.Elapsed)}");
        builder.AppendLine("  seeds ranked by D * (1 - 1/C); nested branches are excluded as counterparts");

        for (var index = 0; index < result.Seeds.Count; index++)
        {
            var item = result.Seeds[index];
            builder.AppendLine();
            builder.AppendLine(
                $"  #{index + 1:N0} seed score {item.Seed.ExcessConcentratedContent:N2}; " +
                $"D {item.Seed.Branch.DuplicateContentCount:N0}; C {item.Seed.Concentration:N2}  " +
                item.Seed.Branch.Path.Value);

            if (item.Counterparts.Count == 0)
            {
                builder.AppendLine("      no disjoint counterpart found");
                continue;
            }

            foreach (var counterpart in item.Counterparts)
            {
                builder.AppendLine(
                    $"      shared {counterpart.SharedDuplicateContentCount,6:N0}; " +
                    $"coverage {counterpart.SeedCoverage,6:P1}/{counterpart.CounterpartCoverage,6:P1}; " +
                    $"Jaccard {counterpart.Jaccard,6:P1}  {counterpart.Branch.Path.Value}");
            }
        }

        return builder.ToString();
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("N3") + " s"
            : elapsed.TotalMilliseconds.ToString("N1") + " ms";
}
