namespace Berries.Core.Analysis;

/// <summary>
/// Parent-relative measurements for finding Group-rich Branches without exhaustively
/// enumerating Branch Pairs. These are objective ranking aids, not semantic classifications.
/// </summary>
public sealed record BranchPriorityMetric(
    BranchRecord Branch,
    double GroupRetention,
    double FileRetention,
    double Concentration,
    double GroupsTimesConcentration,
    double GroupsTimesLogConcentration,
    double ExcessConcentratedGroups);

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
            if (parent.GroupCount <= 0 || parent.FileCount <= 0 || branch.FileCount <= 0)
                continue;

            var groupRetention = (double)branch.GroupCount / parent.GroupCount;
            var fileRetention = (double)branch.FileCount / parent.FileCount;
            if (fileRetention <= 0)
                continue;

            var concentration = groupRetention / fileRetention;
            var groupsTimesConcentration = branch.GroupCount * concentration;
            var groupsTimesLogConcentration = concentration > 1
                ? branch.GroupCount * Math.Log(concentration)
                : 0;
            var excessConcentratedGroups = concentration > 1
                ? branch.GroupCount * (1 - 1 / concentration)
                : 0;

            metrics.Add(new BranchPriorityMetric(
                branch,
                groupRetention,
                fileRetention,
                concentration,
                groupsTimesConcentration,
                groupsTimesLogConcentration,
                excessConcentratedGroups));
        }

        return metrics;
    }
}
