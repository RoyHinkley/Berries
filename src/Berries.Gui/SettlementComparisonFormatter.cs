using System.Text;

namespace Berries.Gui;

internal static class SettlementComparisonFormatter
{
    public static string Format(ProspectiveSettlementComparison comparison)
    {
        var candidate = comparison.Candidate;
        var builder = new StringBuilder();

        builder.AppendLine();
        builder.AppendLine("Prospective whole-DuplicateSet settlement comparison");
        builder.AppendLine($"  candidate filename: {candidate.CommonFileName}");
        builder.AppendLine($"  content: {candidate.DuplicateSet.Content.Value}");
        builder.AppendLine($"  instances/directories: {candidate.InstanceCount:N0}/{candidate.DirectoryCount:N0}");
        builder.AppendLine($"  induced DirectoryPairs: {candidate.InducedDirectoryPairCount:N0}");
        builder.AppendLine($"  other shared Content among those directory pairs: mean {candidate.MeanOtherSharedContent:N2}; max {candidate.MaxOtherSharedContent:N0}");
        builder.AppendLine("  sample instances:");
        foreach (var file in candidate.DuplicateSet.Files.Take(10))
            builder.AppendLine($"    {file.Path.Value}");
        if (candidate.DuplicateSet.Files.Count > 10)
            builder.AppendLine($"    ... {candidate.DuplicateSet.Files.Count - 10:N0} more instance(s)");

        builder.AppendLine("  baseline -> accepted-settlement result (delta):");
        AppendDelta(builder, "DuplicateSet cases",
            comparison.BaselineCaseAnalysis.DuplicateSetCaseCount,
            comparison.SettledCaseAnalysis.DuplicateSetCaseCount);
        AppendDelta(builder, "Single-directory cases",
            comparison.BaselineCaseAnalysis.SingleDirectoryCaseCount,
            comparison.SettledCaseAnalysis.SingleDirectoryCaseCount);
        AppendDelta(builder, "DirectoryPairs",
            comparison.BaselineDirectoryAnalysis.DirectoryPairs.Count,
            comparison.SettledDirectoryAnalysis.DirectoryPairs.Count);
        AppendDelta(builder, "ScopePairs",
            comparison.BaselineScopeAnalysis.ScopePairs.Count,
            comparison.SettledScopeAnalysis.ScopePairs.Count);
        AppendDelta(builder, "total Cases",
            comparison.BaselineCaseAnalysis.TotalCaseCount,
            comparison.SettledCaseAnalysis.TotalCaseCount);
        AppendDelta(builder, "graph components",
            comparison.BaselineDirectoryAnalysis.Graph.ConnectedComponentCount,
            comparison.SettledDirectoryAnalysis.Graph.ConnectedComponentCount);
        AppendDelta(builder, "largest graph component",
            comparison.BaselineDirectoryAnalysis.Graph.LargestComponentSize,
            comparison.SettledDirectoryAnalysis.Graph.LargestComponentSize);

        builder.AppendLine($"  top-case overlap: {comparison.TopCaseOverlapCount:N0}/{comparison.BaselineCaseAnalysis.TopCases.Count:N0}; same rank {comparison.SameRankTopCaseCount:N0}");
        builder.AppendLine($"  settled directory analysis: {FormatElapsed(comparison.SettledDirectoryAnalysis.Timing.Total)}");
        builder.AppendLine($"  settled scope analysis:     {FormatElapsed(comparison.SettledScopeAnalysis.Timing.Total)}");
        builder.AppendLine($"  settled case analysis:      {FormatElapsed(comparison.SettledCaseAnalysis.Timing.Total)}");
        builder.AppendLine($"  comparison rerun total:     {FormatElapsed(comparison.TotalElapsed)}");

        return builder.ToString();
    }

    private static void AppendDelta(StringBuilder builder, string label, int before, int after)
    {
        var delta = after - before;
        var percent = before == 0 ? 0 : (double)(before - after) / before;
        builder.AppendLine($"    {label,-25} {before,10:N0} -> {after,10:N0}  ({delta:+#;-#;0}; {percent,7:P1} reduction)");
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("N3") + " s"
            : elapsed.TotalMilliseconds.ToString("N1") + " ms";
}
