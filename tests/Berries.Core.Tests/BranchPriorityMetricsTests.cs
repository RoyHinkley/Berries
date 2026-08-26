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
            FileCount: 1000,
            DirectoryCount: 100,
            DuplicateFileCount: 500,
            DuplicateContentCount: 100,
            DuplicateDirectoryCount: 50);

        var child = new BranchRecord(
            Path(@"X:\Corpus\Focused"),
            parent.Path,
            FileCount: 100,
            DirectoryCount: 10,
            DuplicateFileCount: 80,
            DuplicateContentCount: 80,
            DuplicateDirectoryCount: 8);

        var metric = Assert.Single(BranchPriorityMetrics.Calculate(new[] { parent, child }));

        Assert.Equal(0.8, metric.DuplicateContentRetention, 12);
        Assert.Equal(0.1, metric.FileRetention, 12);
        Assert.Equal(8.0, metric.Concentration, 12);
        Assert.Equal(640.0, metric.ContentTimesConcentration, 12);
        Assert.Equal(80 * Math.Log(8), metric.ContentTimesLogConcentration, 12);
        Assert.Equal(70.0, metric.ExcessConcentratedContent, 12);
    }

    [Fact]
    public void Calculate_DoesNotRewardBranchesLessConcentratedThanTheirParent()
    {
        var parent = new BranchRecord(
            Path(@"X:\Corpus"), null, 1000, 100, 500, 100, 50);
        var child = new BranchRecord(
            Path(@"X:\Corpus\Diffuse"), parent.Path, 500, 50, 20, 20, 10);

        var metric = Assert.Single(BranchPriorityMetrics.Calculate(new[] { parent, child }));

        Assert.Equal(0.4, metric.Concentration, 12);
        Assert.Equal(0.0, metric.ContentTimesLogConcentration, 12);
        Assert.Equal(0.0, metric.ExcessConcentratedContent, 12);
    }

    private static FileSystemPath Path(string value) => new(value);
}
