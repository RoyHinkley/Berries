using System.Text;
using Berries.Core;
using Berries.Core.Analysis;
using Berries.Core.Cases;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

internal static class CaseReportFormatter
{
    public static string Format(
        CaseAnalysisResult result,
        IReadOnlyList<DuplicateSet> duplicateSets,
        DirectoryAnalysisResult directoryAnalysis,
        ScopeAnalysisResult scopeAnalysis,
        StructuralEvidenceAnalyzer evidenceAnalyzer)
    {
        var builder = new StringBuilder();
        var records = directoryAnalysis.Directories.ToDictionary(record => record.Path);
        var nodes = directoryAnalysis.Graph.Nodes.ToDictionary(node => node.Directory);

        AppendGraphSummary(builder, directoryAnalysis.Graph);
        builder.AppendLine();
        builder.AppendLine($"Cases: {result.TotalCaseCount:N0} total  " +
                           $"[duplicate sets {result.DuplicateSetCaseCount:N0}, " +
                           $"single directories {result.SingleDirectoryCaseCount:N0}, " +
                           $"directory pairs {result.DirectoryPairCaseCount:N0}, " +
                           $"scope pairs {result.ScopePairCaseCount:N0}]");
        builder.AppendLine($"Top {result.TopCases.Count:N0} by leverage; ranking/materialization {FormatElapsed(result.TotalElapsed)}");
        builder.AppendLine();

        for (var index = 0; index < result.TopCases.Count; index++)
        {
            var item = result.TopCases[index];
            builder.AppendLine($"#{index + 1:N0}  {Kind(item)}  leverage {item.Leverage:N0}");

            switch (item)
            {
                case DuplicateSetCase duplicate:
                    AppendDuplicateSet(builder, duplicate);
                    break;

                case SingleDirectoryCase directory:
                    AppendSingleDirectory(builder, directory, nodes);
                    break;

                case DirectoryPairCase pair:
                    AppendDirectoryPair(builder, pair.Pair, records, nodes);
                    break;

                case ScopePairCase pair:
                    AppendScopePair(
                        builder,
                        pair,
                        duplicateSets,
                        directoryAnalysis,
                        scopeAnalysis,
                        evidenceAnalyzer,
                        records,
                        nodes);
                    break;
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void AppendGraphSummary(StringBuilder builder, DirectoryGraphAnalysis graph)
    {
        builder.AppendLine("Directory-pair graph");
        builder.AppendLine($"  directories in portrait: {graph.TotalDirectoryCount:N0}");
        builder.AppendLine($"  directories containing duplicated content: {graph.DuplicateDirectoryCount:N0}");
        builder.AppendLine($"  directories with internal duplicates: {graph.InternalDuplicateDirectoryCount:N0}");
        builder.AppendLine($"  directories participating in DirectoryPairs: {graph.PairParticipatingDirectoryCount:N0}");
        builder.AppendLine($"  DirectoryPairs: {graph.DirectoryPairCount:N0}");
        builder.AppendLine($"  connected components (including isolated duplicate directories): {graph.ConnectedComponentCount:N0}");
        builder.AppendLine($"  largest component: {graph.LargestComponentSize:N0} directories");
        builder.AppendLine($"  pair density among participating directories: {graph.PairDensity:P3}");

        builder.AppendLine("  strongest hubs by degree:");
        foreach (var node in graph.Nodes.Where(node => node.Degree > 0).Take(10))
        {
            builder.AppendLine($"    degree {node.Degree,5:N0}  weighted {node.WeightedDegree,7:N0}  " +
                               $"max-edge {node.MaxPairLeverage,5:N0}  {node.Directory.Value}");
        }
    }

    private static void AppendDuplicateSet(StringBuilder builder, DuplicateSetCase item)
    {
        builder.AppendLine($"  content: {item.DuplicateSet.Content}");
        builder.AppendLine($"  instances: {item.DuplicateSet.InstanceCount:N0}");
        foreach (var file in item.DuplicateSet.Files.Take(12))
            builder.AppendLine($"    {file.Path.Value}");
        if (item.DuplicateSet.Files.Count > 12)
            builder.AppendLine($"    ... {item.DuplicateSet.Files.Count - 12:N0} more instance(s)");
    }

    private static void AppendSingleDirectory(
        StringBuilder builder,
        SingleDirectoryCase item,
        IReadOnlyDictionary<FileSystemPath, DirectoryGraphNode> nodes)
    {
        builder.AppendLine($"  directory: {item.Directory.Value}");
        builder.AppendLine($"  bounded files: {item.Files.Count:N0}; internally duplicated contents: {item.DuplicateContentCount:N0}");

        if (nodes.TryGetValue(item.Directory, out var node))
        {
            builder.AppendLine($"  graph: degree {node.Degree:N0}; weighted degree {node.WeightedDegree:N0}; " +
                               $"max pair leverage {node.MaxPairLeverage:N0}; duplicated contents {node.DuplicateContentCount:N0}");
        }
    }

    private static void AppendDirectoryPair(
        StringBuilder builder,
        DirectoryPair pair,
        IReadOnlyDictionary<FileSystemPath, DirectoryRecord> records,
        IReadOnlyDictionary<FileSystemPath, DirectoryGraphNode> nodes)
    {
        builder.AppendLine($"  first:  {pair.First.Value}");
        builder.AppendLine($"  second: {pair.Second.Value}");
        AppendDirectoryPairStats(builder, pair, records, nodes, "  ");
    }

    private static void AppendScopePair(
        StringBuilder builder,
        ScopePairCase item,
        IReadOnlyList<DuplicateSet> duplicateSets,
        DirectoryAnalysisResult directoryAnalysis,
        ScopeAnalysisResult scopeAnalysis,
        StructuralEvidenceAnalyzer evidenceAnalyzer,
        IReadOnlyDictionary<FileSystemPath, DirectoryRecord> records,
        IReadOnlyDictionary<FileSystemPath, DirectoryGraphNode> nodes)
    {
        var pair = item.Pair;
        var evidence = evidenceAnalyzer.AnalyzeScopePair(
            pair,
            duplicateSets,
            directoryAnalysis.DirectoryPairs,
            scopeAnalysis.ScopePairs,
            10);

        builder.AppendLine($"  first:  {pair.FirstRoot.Value}");
        builder.AppendLine($"  second: {pair.SecondRoot.Value}");
        builder.AppendLine($"  roots nested: {(evidence.RootsNested ? "yes" : "no")}");
        builder.AppendLine($"  bounded files: {item.Files.Count:N0}; contributing directory pairs: {pair.DirectoryPairCount:N0}");
        builder.AppendLine($"  duplicated contents on effective sides: " +
                           $"{evidence.FirstSideDuplicateContentCount:N0} / {evidence.SecondSideDuplicateContentCount:N0}");
        builder.AppendLine($"  cross-side coverage: " +
                           $"{Ratio(pair.Leverage, evidence.FirstSideDuplicateContentCount):P1} / " +
                           $"{Ratio(pair.Leverage, evidence.SecondSideDuplicateContentCount):P1}");
        builder.AppendLine($"  subsidiary ScopePairs: {evidence.SubsidiaryScopePairCount:N0}");

        if (evidence.StrongestSubsidiaryScopePairs.Count > 0)
        {
            builder.AppendLine("  strongest subsidiary ScopePairs:");
            foreach (var subsidiary in evidence.StrongestSubsidiaryScopePairs)
            {
                builder.AppendLine($"    L {subsidiary.Leverage,5:N0}  DP {subsidiary.DirectoryPairCount,5:N0}  " +
                                   $"{subsidiary.FirstRoot.Value}");
                builder.AppendLine($"                       ↔ {subsidiary.SecondRoot.Value}");
            }
        }

        if (evidence.StrongestContributingDirectoryPairs.Count > 0)
        {
            builder.AppendLine("  strongest contributing DirectoryPairs:");
            foreach (var directoryPair in evidence.StrongestContributingDirectoryPairs)
            {
                builder.AppendLine($"    L {directoryPair.Leverage,5:N0}  {directoryPair.First.Value}");
                builder.AppendLine($"                       ↔ {directoryPair.Second.Value}");
                AppendDirectoryPairStats(builder, directoryPair, records, nodes, "      ");
            }
        }
    }

    private static void AppendDirectoryPairStats(
        StringBuilder builder,
        DirectoryPair pair,
        IReadOnlyDictionary<FileSystemPath, DirectoryRecord> records,
        IReadOnlyDictionary<FileSystemPath, DirectoryGraphNode> nodes,
        string indent)
    {
        if (!records.TryGetValue(pair.First, out var first)
            || !records.TryGetValue(pair.Second, out var second))
            return;

        var firstCoverage = Ratio(pair.Leverage, first.DuplicateContentCount);
        var secondCoverage = Ratio(pair.Leverage, second.DuplicateContentCount);
        var union = first.DuplicateContentCount + second.DuplicateContentCount - pair.Leverage;
        var jaccard = Ratio(pair.Leverage, union);
        var firstDegree = nodes.TryGetValue(pair.First, out var firstNode) ? firstNode.Degree : 0;
        var secondDegree = nodes.TryGetValue(pair.Second, out var secondNode) ? secondNode.Degree : 0;

        builder.AppendLine($"{indent}shared {pair.Leverage:N0}; coverage {firstCoverage:P1}/{secondCoverage:P1}; " +
                           $"Jaccard {jaccard:P1}; degrees {firstDegree:N0}/{secondDegree:N0}");
    }

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : (double)numerator / denominator;

    private static string Kind(Case item) => item switch
    {
        DuplicateSetCase => "DuplicateSet",
        SingleDirectoryCase => "SingleDirectory",
        DirectoryPairCase => "DirectoryPair",
        ScopePairCase => "ScopePair",
        _ => item.GetType().Name
    };

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("N3") + " s"
            : elapsed.TotalMilliseconds.ToString("N1") + " ms";
}
