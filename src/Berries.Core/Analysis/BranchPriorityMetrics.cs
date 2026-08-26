namespace Berries.Core.Analysis;

/// <summary>
/// Experimental parent-relative measurements for finding duplication-rich branches
/// without enumerating BranchPairs. These are objective ranking aids, not Case semantics.
/// </summary>
public sealed record BranchPriorityMetric(
    BranchRecord Branch,
    double DuplicateContentRetention,
    double FileRetention,
    double Concentration,
    double ContentTimesConcentration,
    double ContentTimesLogConcentration,
    double ExcessConcentratedContent);

public static class BranchPriorityMetrics
{
    public static IReadOnlyList<BranchPriorityMetric> Calculate(IReadOnlyList<BranchRecord> branches)
    {
        var byPath = branches.ToDictionary(branch => branch.Path);
        var metrics = new List<BranchPriorityMetric>();

        foreach (var branch in branches)
        {
            if (branch.ParentPath is not { } parentPath || !byPath.TryGetValue(parentPath, out var parent))
                continue;
            if (parent.DuplicateContentCount <= 0 || parent.FileCount <= 0 || branch.FileCount <= 0)
                continue;

            var contentRetention = (double)branch.DuplicateContentCount / parent.DuplicateContentCount;
            var fileRetention = (double)branch.FileCount / parent.FileCount;
            if (fileRetention <= 0)
                continue;

            var concentration = contentRetention / fileRetention;
            var contentTimesConcentration = branch.DuplicateContentCount * concentration;
            var contentTimesLogConcentration = concentration > 1
                ? branch.DuplicateContentCount * Math.Log(concentration)
                : 0;
            var excessConcentratedContent = concentration > 1
                ? branch.DuplicateContentCount * (1 - 1 / concentration)
                : 0;

            metrics.Add(new BranchPriorityMetric(
                branch,
                contentRetention,
                fileRetention,
                concentration,
                contentTimesConcentration,
                contentTimesLogConcentration,
                excessConcentratedContent));
        }

        return metrics;
    }
}
