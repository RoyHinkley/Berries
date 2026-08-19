using System.Text;
using Berries.Core.Analysis;
using Berries.Core.Cases;
using Berries.Core.Domain;

namespace Berries.Gui;

internal static class CaseReportFormatter
{
    public static string Format(
        CaseAnalysisResult result,
        IReadOnlyList<DuplicateSet> duplicateSets)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Cases: {result.TotalCaseCount:N0} total  " +
                           $"[duplicate sets {result.DuplicateSetCaseCount:N0}, " +
                           $"single directories {result.SingleDirectoryCaseCount:N0}, " +
                           $"directory pairs {result.DirectoryPairCaseCount:N0}, " +
                           $"scope pairs {result.ScopePairCaseCount:N0}]");
        builder.AppendLine($"Top {result.TopCases.Count:N0} by leverage");
        builder.AppendLine();

        for (var index = 0; index < result.TopCases.Count; index++)
        {
            var item = result.TopCases[index];
            var bounded = item.Files.ToHashSet();
            var representedSets = duplicateSets
                .Where(set => set.Files.Any(bounded.Contains))
                .ToArray();
            var duplicateFiles = representedSets
                .SelectMany(set => set.Files)
                .Where(bounded.Contains)
                .Distinct()
                .OrderBy(file => file.Path.Value, StringComparer.Ordinal)
                .ToArray();

            builder.AppendLine($"#{index + 1:N0}  {Kind(item)}  leverage {item.Leverage:N0}");
            AppendIdentity(builder, item);
            builder.AppendLine($"  bounded files: {item.Files.Count:N0}; " +
                               $"duplicated files represented: {duplicateFiles.Length:N0}; " +
                               $"duplicated contents represented: {representedSets.Length:N0}");

            foreach (var file in duplicateFiles.Take(12))
                builder.AppendLine($"    {file.Path.Value}");

            if (duplicateFiles.Length > 12)
                builder.AppendLine($"    ... {duplicateFiles.Length - 12:N0} more duplicated file(s)");

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Kind(Case item) => item switch
    {
        DuplicateSetCase => "DuplicateSet",
        SingleDirectoryCase => "SingleDirectory",
        DirectoryPairCase => "DirectoryPair",
        ScopePairCase => "ScopePair",
        _ => item.GetType().Name
    };

    private static void AppendIdentity(StringBuilder builder, Case item)
    {
        switch (item)
        {
            case DuplicateSetCase duplicate:
                builder.AppendLine($"  content: {duplicate.DuplicateSet.Content}");
                builder.AppendLine($"  instances: {duplicate.DuplicateSet.InstanceCount:N0}");
                break;

            case SingleDirectoryCase directory:
                builder.AppendLine($"  directory: {directory.Directory.Value}");
                builder.AppendLine($"  internally duplicated contents: {directory.DuplicateContentCount:N0}");
                break;

            case DirectoryPairCase pair:
                builder.AppendLine($"  first:  {pair.Pair.First.Value}");
                builder.AppendLine($"  second: {pair.Pair.Second.Value}");
                builder.AppendLine($"  directly shared contents: {pair.Pair.SharedContentCount:N0}");
                break;

            case ScopePairCase pair:
                builder.AppendLine($"  first:  {pair.Pair.FirstRoot.Value}");
                builder.AppendLine($"  second: {pair.Pair.SecondRoot.Value}");
                builder.AppendLine($"  contributing directory pairs: {pair.Pair.DirectoryPairCount:N0}");
                break;
        }
    }
}
