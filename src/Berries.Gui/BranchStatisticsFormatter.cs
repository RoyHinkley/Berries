using System.Text;
using Berries.Core.Analysis;

namespace Berries.Gui;

internal static class BranchStatisticsFormatter
{
    public static string Format(BranchStatisticsResult result, int limit = 25)
    {
        var builder = new StringBuilder();
        var byPath = result.Branches.ToDictionary(branch => branch.Path);

        builder.AppendLine();
        builder.AppendLine("Branch statistics");
        builder.AppendLine($"  duplicate-bearing branches: {result.Branches.Count:N0}");
        builder.AppendLine($"  analysis time: {FormatElapsed(result.Elapsed)}");
        builder.AppendLine($"  top {Math.Min(limit, result.Branches.Count):N0} by distinct duplicated Content:");

        foreach (var branch in result.Branches.Take(limit))
        {
            var duplicateFileFraction = branch.FileCount == 0
                ? 0
                : (double)branch.DuplicateFileCount / branch.FileCount;
            var duplicateDirectoryFraction = branch.DirectoryCount == 0
                ? 0
                : (double)branch.DuplicateDirectoryCount / branch.DirectoryCount;

            builder.AppendLine(
                $"    contents {branch.DuplicateContentCount,6:N0}  " +
                $"dup-files {branch.DuplicateFileCount,7:N0}/{branch.FileCount,-7:N0} ({duplicateFileFraction,6:P1})  " +
                $"dup-dirs {branch.DuplicateDirectoryCount,5:N0}/{branch.DirectoryCount,-5:N0} ({duplicateDirectoryFraction,6:P1})  " +
                branch.Path.Value);

            if (branch.ParentPath is { } parentPath && byPath.TryGetValue(parentPath, out var parent))
            {
                var contentRetention = parent.DuplicateContentCount == 0
                    ? 0
                    : (double)branch.DuplicateContentCount / parent.DuplicateContentCount;
                var fileRetention = parent.FileCount == 0
                    ? 0
                    : (double)branch.FileCount / parent.FileCount;
                var directoryRetention = parent.DirectoryCount == 0
                    ? 0
                    : (double)branch.DirectoryCount / parent.DirectoryCount;

                builder.AppendLine(
                    $"      from parent: duplicated Content {contentRetention,6:P1}; " +
                    $"files {fileRetention,6:P1}; directories {directoryRetention,6:P1}");
            }
        }

        AppendSeedRankings(builder, BranchPriorityMetrics.Calculate(result.Branches));
        return builder.ToString();
    }

    private static void AppendSeedRankings(
        StringBuilder builder,
        IReadOnlyList<BranchPriorityMetric> metrics)
    {
        const int rankingLimit = 50;

        builder.AppendLine();
        builder.AppendLine("Experimental container-centric seed rankings");
        builder.AppendLine("  C = duplicated-Content retention / file retention, relative to parent");
        builder.AppendLine("  D = distinct duplicated Content in branch");
        builder.AppendLine("  candidate measures: D*C; D*ln(C) for C>1; D*(1-1/C) for C>1");
        builder.AppendLine("  These are exploratory ranking aids only; no cutoff or preferred formula is assumed.");

        AppendMetricRanking(
            builder,
            "Concentration C",
            metrics.OrderByDescending(item => item.Concentration)
                .ThenByDescending(item => item.Branch.DuplicateContentCount)
                .ToArray(),
            item => item.Concentration,
            rankingLimit);

        AppendMetricRanking(
            builder,
            "D * C",
            metrics.OrderByDescending(item => item.ContentTimesConcentration)
                .ThenByDescending(item => item.Branch.DuplicateContentCount)
                .ToArray(),
            item => item.ContentTimesConcentration,
            rankingLimit);

        AppendMetricRanking(
            builder,
            "D * ln(C)",
            metrics.OrderByDescending(item => item.ContentTimesLogConcentration)
                .ThenByDescending(item => item.Branch.DuplicateContentCount)
                .ToArray(),
            item => item.ContentTimesLogConcentration,
            rankingLimit);

        AppendMetricRanking(
            builder,
            "D * (1 - 1/C)",
            metrics.OrderByDescending(item => item.ExcessConcentratedContent)
                .ThenByDescending(item => item.Branch.DuplicateContentCount)
                .ToArray(),
            item => item.ExcessConcentratedContent,
            rankingLimit);
    }

    private static void AppendMetricRanking(
        StringBuilder builder,
        string title,
        IReadOnlyList<BranchPriorityMetric> ordered,
        Func<BranchPriorityMetric, double> score,
        int limit)
    {
        builder.AppendLine();
        builder.AppendLine($"  {title} — top {Math.Min(limit, ordered.Count):N0}");
        builder.AppendLine("    rank        score       D       C   content-retained   files-retained  branch");

        for (var index = 0; index < Math.Min(limit, ordered.Count); index++)
        {
            var item = ordered[index];
            builder.AppendLine(
                $"    {index + 1,4:N0}  {score(item),11:N2}  {item.Branch.DuplicateContentCount,6:N0}  " +
                $"{item.Concentration,6:N2}  {item.DuplicateContentRetention,15:P1}  " +
                $"{item.FileRetention,14:P1}  {item.Branch.Path.Value}");
        }

        builder.AppendLine("    falloff samples:");
        foreach (var rank in FalloffRanks(ordered.Count))
        {
            var item = ordered[rank - 1];
            builder.AppendLine(
                $"      #{rank,-5:N0} score {score(item),11:N2}  D {item.Branch.DuplicateContentCount,6:N0}  " +
                $"C {item.Concentration,7:N2}  {item.Branch.Path.Value}");
        }
    }

    private static IEnumerable<int> FalloffRanks(int count)
    {
        var requested = new[] { 1, 2, 3, 5, 10, 20, 30, 50, 75, 100, 150, 200, 300, 500, 750, 1000 };
        foreach (var rank in requested)
        {
            if (rank <= count)
                yield return rank;
        }

        if (count > 0 && !requested.Contains(count))
            yield return count;
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("N3") + " s"
            : elapsed.TotalMilliseconds.ToString("N1") + " ms";
}
