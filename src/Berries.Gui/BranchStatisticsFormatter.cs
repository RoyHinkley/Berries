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

        return builder.ToString();
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("N3") + " s"
            : elapsed.TotalMilliseconds.ToString("N1") + " ms";
}
