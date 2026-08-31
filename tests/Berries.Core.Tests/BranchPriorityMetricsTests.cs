using Berries.Core.Analysis;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class BranchPriorityMetricsTests
{
    [Fact]
    public void Calculate_DerivesParentRelativeConcentrationAndScaledMeasures()
    {
        var parent = new BranchRecord(
            Path(@"X:\Corpus"),
            null,
            UniqueFileCount: 500,
            DirectoryCount: 100,
            GroupedFileCount: 500,
            GroupCount: 100,
            GroupedDirectoryCount: 50);

        var child = new BranchRecord(
            Path(@"X:\Corpus\Focused"),
            parent.Path,
            UniqueFileCount: 20,
            DirectoryCount: 10,
            GroupedFileCount: 80,
            GroupCount: 80,
            GroupedDirectoryCount: 8);

        var metric = Assert.Single(BranchPriorityMetrics.Calculate(new[] { parent, child }));

        Assert.Equal(0.8, metric.GroupRetention, 12);
        Assert.Equal(0.1, metric.FileRetention, 12);
        Assert.Equal(8.0, metric.Concentration, 12);
        Assert.Equal(640.0, metric.GroupsTimesConcentration, 12);
        Assert.Equal(80 * Math.Log(8), metric.GroupsTimesLogConcentration, 12);
        Assert.Equal(70.0, metric.ExcessConcentratedGroups, 12);
    }

    [Fact]
    public void Calculate_DoesNotRewardBranchesLessConcentratedThanTheirParent()
    {
        var parent = new BranchRecord(
            Path(@"X:\Corpus"), null, 500, 100, 500, 100, 50);
        var child = new BranchRecord(
            Path(@"X:\Corpus\Diffuse"), parent.Path, 480, 50, 20, 20, 10);

        var metric = Assert.Single(BranchPriorityMetrics.Calculate(new[] { parent, child }));

        Assert.Equal(0.4, metric.Concentration, 12);
        Assert.Equal(0.0, metric.GroupsTimesLogConcentration, 12);
        Assert.Equal(0.0, metric.ExcessConcentratedGroups, 12);
    }

    private static FileSystemPath Path(string value) => new(value);
}
