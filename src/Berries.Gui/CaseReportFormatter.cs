using System.Diagnostics;
using System.Text;
using Berries.Core;
using Berries.Core.Analysis;
using Berries.Core.Cases;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

internal static class CaseReportFormatter
{
    public static string Format(
        ScanResult scan,
        DuplicateDiscoveryResult duplicateDiscovery,
        CaseAnalysisResult result,
        IReadOnlyList<FileInstance> portraitFiles,
        IReadOnlyList<DuplicateSet> duplicateSets,
        DirectoryAnalysisResult directoryAnalysis,
        ScopeAnalysisResult scopeAnalysis,
        StructuralEvidenceAnalyzer evidenceAnalyzer)
    {
        var reportTimer = Stopwatch.StartNew();
        var builder = new StringBuilder();
        var records = directoryAnalysis.Directories.ToDictionary(record => record.Path);
        var nodes = directoryAnalysis.Graph.Nodes.ToDictionary(node => node.Directory);

        AppendRunSummary(builder, scan, duplicateDiscovery, directoryAnalysis, scopeAnalysis, result);
        builder.AppendLine();
        AppendGraphSummary(builder, directoryAnalysis.Graph);
        builder.AppendLine();
        builder.AppendLine($"Cases: {result.TotalCaseCount:N0} total  " +
                           $"[duplicate sets {result.DuplicateSetCaseCount:N0}, " +
                           $"single directories {result.SingleDirectoryCaseCount:N0}, " +
                           $"directory pairs {result.DirectoryPairCaseCount:N0}, " +
                           $"scope pairs {result.ScopePairCaseCount:N0}]");
        AppendLeverageDistributions(builder, duplicateSets, directoryAnalysis, scopeAnalysis);
        builder.AppendLine($"Top {result.TopCases.Count:N0} by leverage");
        builder.AppendLine();

        var evidenceCases = 0;
        var evidenceTotal = TimeSpan.Zero;
        var contributingTotal = TimeSpan.Zero;
        var subsidiariesTotal = TimeSpan.Zero;
        var duplicateCountsTotal = TimeSpan.Zero;
        var parentBreadthTotal = TimeSpan.Zero;
        var subsidiaryBreadthTotal = TimeSpan.Zero;

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
                    var timing = AppendScopePair(builder, pair, portraitFiles, duplicateSets,
                        directoryAnalysis, scopeAnalysis, evidenceAnalyzer, records, nodes);
                    evidenceCases++;
                    evidenceTotal += timing.Total;
                    contributingTotal += timing.ContributingDirectoryPairs;
                    subsidiariesTotal += timing.SubsidiaryScopePairs;
                    duplicateCountsTotal += timing.DuplicateContentCounts;
                    parentBreadthTotal += timing.ParentBreadth;
                    subsidiaryBreadthTotal += timing.SubsidiaryBreadth;
                    break;
            }
            builder.AppendLine();
        }

        reportTimer.Stop();
        builder.AppendLine("Report-generation timing");
        builder.AppendLine($"  sampled ScopePair evidence: {FormatElapsed(evidenceTotal)} across {evidenceCases:N0} case(s)");
        builder.AppendLine($"    contributing DirectoryPair scans: {FormatElapsed(contributingTotal)}");
        builder.AppendLine($"    subsidiary ScopePair scans:       {FormatElapsed(subsidiariesTotal)}");
        builder.AppendLine($"    duplicate-content counts:         {FormatElapsed(duplicateCountsTotal)}");
        builder.AppendLine($"    parent breadth:                    {FormatElapsed(parentBreadthTotal)}");
        builder.AppendLine($"    subsidiary breadth:                {FormatElapsed(subsidiaryBreadthTotal)}");
        builder.AppendLine($"  total report generation: {FormatElapsed(reportTimer.Elapsed)}");

        return builder.ToString();
    }

    private static void AppendRunSummary(
        StringBuilder builder,
        ScanResult scan,
        DuplicateDiscoveryResult duplicateDiscovery,
        DirectoryAnalysisResult directoryAnalysis,
        ScopeAnalysisResult scopeAnalysis,
        CaseAnalysisResult caseAnalysis)
    {
        builder.AppendLine("Run summary");
        builder.AppendLine($"  corpus roots: {scan.Roots.Count:N0}");
        foreach (var root in scan.Roots)
            builder.AppendLine($"    {root}");
        builder.AppendLine($"  portrait after scan: {scan.FileCount:N0} files; {scan.TotalBytes:N0} bytes");
        builder.AppendLine($"  current portrait after duplicate discovery: {duplicateDiscovery.Portrait.Files.Count:N0} files; " +
                           $"{duplicateDiscovery.Portrait.Files.Sum(file => file.Length):N0} bytes; " +
                           $"evictions {duplicateDiscovery.Evictions.Count:N0}");
        builder.AppendLine($"  duplicate sets: {duplicateDiscovery.DuplicateSets.Count:N0}; duplicate files: {duplicateDiscovery.DuplicateFileCount:N0}");
        builder.AppendLine($"  analyzed directories: {directoryAnalysis.Directories.Count:N0}; DirectoryPairs: {directoryAnalysis.DirectoryPairs.Count:N0}; ScopePairs: {scopeAnalysis.ScopePairs.Count:N0}");
        builder.AppendLine("  measured phase times:");
        builder.AppendLine($"    scan total:              {FormatElapsed(scan.TotalElapsed)}  [normalize {FormatElapsed(scan.CorpusNormalizationElapsed)}, portrait {FormatElapsed(scan.PortraitAcquisitionElapsed)}]");
        builder.AppendLine($"    duplicate discovery:     {FormatElapsed(duplicateDiscovery.Timing.Total)}  [size grouping {FormatElapsed(duplicateDiscovery.Timing.SizeGrouping)}, hashing {FormatElapsed(duplicateDiscovery.Timing.ContentHashing)}, set construction {FormatElapsed(duplicateDiscovery.Timing.DuplicateSetConstruction)}]");
        builder.AppendLine($"    directory analysis:      {FormatElapsed(directoryAnalysis.Timing.Total)}  [records {FormatElapsed(directoryAnalysis.Timing.DirectoryRecords)}, pairs {FormatElapsed(directoryAnalysis.Timing.DirectoryPairs)}]");
        builder.AppendLine($"    scope analysis:          {FormatElapsed(scopeAnalysis.Timing.Total)}  [evidence {FormatElapsed(scopeAnalysis.Timing.EvidenceConstruction)}, aggregation {FormatElapsed(scopeAnalysis.Timing.ScopeAggregation)}, results {FormatElapsed(scopeAnalysis.Timing.ResultConstruction)}]");
        builder.AppendLine($"    case analysis:           {FormatElapsed(caseAnalysis.Timing.Total)}  [candidates {FormatElapsed(caseAnalysis.Timing.CandidateConstruction)}, ranking {FormatElapsed(caseAnalysis.Timing.Ranking)}, materialization {FormatElapsed(caseAnalysis.Timing.Materialization)}]");
        var subtotal = scan.TotalElapsed + duplicateDiscovery.Timing.Total + directoryAnalysis.Timing.Total + scopeAnalysis.Timing.Total + caseAnalysis.Timing.Total;
        builder.AppendLine($"    measured pipeline subtotal (before report): {FormatElapsed(subtotal)}");
    }

    private static void AppendLeverageDistributions(
        StringBuilder builder,
        IReadOnlyList<DuplicateSet> duplicateSets,
        DirectoryAnalysisResult directoryAnalysis,
        ScopeAnalysisResult scopeAnalysis)
    {
        builder.AppendLine("Leverage distributions (min / Q1 / median / Q3 / max):");
        AppendDistribution(builder, "DuplicateSet", Enumerable.Repeat(1, duplicateSets.Count));
        AppendDistribution(builder, "SingleDirectory", GetSingleDirectoryLeverages(duplicateSets));
        AppendDistribution(builder, "DirectoryPair", directoryAnalysis.DirectoryPairs.Select(item => item.Leverage));
        AppendDistribution(builder, "ScopePair", scopeAnalysis.ScopePairs.Select(item => item.Leverage));
    }

    private static IEnumerable<int> GetSingleDirectoryLeverages(IReadOnlyList<DuplicateSet> duplicateSets)
    {
        var contents = new Dictionary<FileSystemPath, int>();
        foreach (var set in duplicateSets)
        {
            foreach (var group in set.Files.GroupBy(file => file.ParentDirectory))
            {
                if (group.Count() >= 2)
                    contents[group.Key] = contents.GetValueOrDefault(group.Key) + 1;
            }
        }
        return contents.Values;
    }

    private static void AppendDistribution(StringBuilder builder, string name, IEnumerable<int> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            builder.AppendLine($"  {name,-16} —");
            return;
        }
        builder.AppendLine($"  {name,-16} {ordered[0],5:N0} / {Quantile(ordered, .25),5:N0} / " +
                           $"{Quantile(ordered, .50),5:N0} / {Quantile(ordered, .75),5:N0} / {ordered[^1],5:N0}");
    }

    private static int Quantile(int[] ordered, double fraction) =>
        ordered[(int)Math.Round((ordered.Length - 1) * fraction, MidpointRounding.AwayFromZero)];

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
            builder.AppendLine($"    degree {node.Degree,5:N0}  weighted {node.WeightedDegree,7:N0}  mean-edge {node.MeanPairLeverage,6:N2}  max-edge {node.MaxPairLeverage,5:N0}  concentration {node.StrongestPairConcentration,6:P1}  {node.Directory.Value}");
    }

    private static void AppendDuplicateSet(StringBuilder builder, DuplicateSetCase item)
    {
        builder.AppendLine($"  content: {item.DuplicateSet.Content}");
        builder.AppendLine($"  instances: {item.DuplicateSet.InstanceCount:N0}");
        foreach (var file in item.DuplicateSet.Files.Take(12)) builder.AppendLine($"    {file.Path.Value}");
        if (item.DuplicateSet.Files.Count > 12) builder.AppendLine($"    ... {item.DuplicateSet.Files.Count - 12:N0} more instance(s)");
    }

    private static void AppendSingleDirectory(StringBuilder builder, SingleDirectoryCase item,
        IReadOnlyDictionary<FileSystemPath, DirectoryGraphNode> nodes)
    {
        builder.AppendLine($"  directory: {item.Directory.Value}");
        builder.AppendLine($"  bounded files: {item.Files.Count:N0}; internally duplicated contents: {item.DuplicateContentCount:N0}");
        if (nodes.TryGetValue(item.Directory, out var node))
            builder.AppendLine($"  graph: degree {node.Degree:N0}; weighted degree {node.WeightedDegree:N0}; mean pair leverage {node.MeanPairLeverage:N2}; max pair leverage {node.MaxPairLeverage:N0}; strongest-pair concentration {node.StrongestPairConcentration:P1}; duplicated contents {node.DuplicateContentCount:N0}");
    }

    private static void AppendDirectoryPair(StringBuilder builder, DirectoryPair pair,
        IReadOnlyDictionary<FileSystemPath, DirectoryRecord> records,
        IReadOnlyDictionary<FileSystemPath, DirectoryGraphNode> nodes)
    {
        builder.AppendLine($"  first:  {pair.First.Value}");
        builder.AppendLine($"  second: {pair.Second.Value}");
        AppendDirectoryPairStats(builder, pair, records, nodes, "  ");
    }

    private static ScopePairEvidenceTiming AppendScopePair(StringBuilder builder, ScopePairCase item,
        IReadOnlyList<FileInstance> portraitFiles, IReadOnlyList<DuplicateSet> duplicateSets,
        DirectoryAnalysisResult directoryAnalysis, ScopeAnalysisResult scopeAnalysis,
        StructuralEvidenceAnalyzer evidenceAnalyzer,
        IReadOnlyDictionary<FileSystemPath, DirectoryRecord> records,
        IReadOnlyDictionary<FileSystemPath, DirectoryGraphNode> nodes)
    {
        var pair = item.Pair;
        var evidence = evidenceAnalyzer.AnalyzeScopePair(pair, portraitFiles, duplicateSets,
            directoryAnalysis.DirectoryPairs, scopeAnalysis.ScopePairs, 10);
        var minimumCoverage = Math.Min(Ratio(pair.Leverage, evidence.FirstSideDuplicateContentCount), Ratio(pair.Leverage, evidence.SecondSideDuplicateContentCount));
        var maximumCoverage = Math.Max(Ratio(pair.Leverage, evidence.FirstSideDuplicateContentCount), Ratio(pair.Leverage, evidence.SecondSideDuplicateContentCount));

        builder.AppendLine($"  first:  {pair.FirstRoot.Value}");
        builder.AppendLine($"  second: {pair.SecondRoot.Value}");
        builder.AppendLine($"  roots nested: {(evidence.RootsNested ? "yes" : "no")}");
        builder.AppendLine($"  side breadth: first {evidence.FirstSideBreadth.DirectoryCount:N0} dirs / {evidence.FirstSideBreadth.FileCount:N0} files; " +
                           $"second {evidence.SecondSideBreadth.DirectoryCount:N0} dirs / {evidence.SecondSideBreadth.FileCount:N0} files");
        builder.AppendLine($"  crossing-evidence directories: {evidence.FirstSideBreadth.CrossingDirectoryCount:N0} / {evidence.SecondSideBreadth.CrossingDirectoryCount:N0}; contributing directory pairs: {pair.DirectoryPairCount:N0}; weighted direct evidence {evidence.ContributingWeightedLeverage:N0}");
        builder.AppendLine($"  duplicated contents on effective sides: {evidence.FirstSideDuplicateContentCount:N0} / {evidence.SecondSideDuplicateContentCount:N0}");
        builder.AppendLine($"  cross-side coverage: {Ratio(pair.Leverage, evidence.FirstSideDuplicateContentCount):P1} / {Ratio(pair.Leverage, evidence.SecondSideDuplicateContentCount):P1}; " +
                           $"min {minimumCoverage:P1}; max {maximumCoverage:P1}; asymmetry {CoverageAsymmetry(minimumCoverage, maximumCoverage):P1}");
        builder.AppendLine($"  direct-evidence concentration: top 1 {evidence.StrongestDirectoryPairConcentration:P1}; top 5 {evidence.TopFiveDirectoryPairConcentration:P1}; top 10 {evidence.TopTenDirectoryPairConcentration:P1}");
        builder.AppendLine($"  subsidiary ScopePairs: {evidence.SubsidiaryScopePairCount:N0}; leverage plateau ≥90% {evidence.SubsidiariesAtNinetyPercentLeverage:N0}, ≥95% {evidence.SubsidiariesAtNinetyFivePercentLeverage:N0}, ≥99% {evidence.SubsidiariesAtNinetyNinePercentLeverage:N0}");
        if (evidence.StrongestSubsidiaryScopePairs.Count > 0)
        {
            builder.AppendLine("  strongest subsidiary ScopePairs:");
            foreach (var summary in evidence.StrongestSubsidiaryScopePairs)
            {
                var subsidiary = summary.Pair;
                var parentDirectories = evidence.FirstSideBreadth.DirectoryCount + evidence.SecondSideBreadth.DirectoryCount;
                var parentFiles = evidence.FirstSideBreadth.FileCount + evidence.SecondSideBreadth.FileCount;
                var childDirectories = summary.FirstSideBreadth.DirectoryCount + summary.SecondSideBreadth.DirectoryCount;
                var childFiles = summary.FirstSideBreadth.FileCount + summary.SecondSideBreadth.FileCount;
                builder.AppendLine($"    L {subsidiary.Leverage,5:N0}  ratio {Ratio(subsidiary.Leverage, pair.Leverage),6:P1}  DP {subsidiary.DirectoryPairCount,5:N0}  evidence {Ratio(subsidiary.DirectoryPairCount, pair.DirectoryPairCount),6:P1}  " +
                                   $"depth +{summary.FirstRootDepthChange}/+{summary.SecondRootDepthChange}  dirs {childDirectories,6:N0} ({Reduction(childDirectories, parentDirectories),6:P1} less)  " +
                                   $"files {childFiles,8:N0} ({Reduction(childFiles, parentFiles),6:P1} less)  {subsidiary.FirstRoot.Value}");
                builder.AppendLine($"                       ↔ {subsidiary.SecondRoot.Value}");
                builder.AppendLine($"      sides: {summary.FirstSideBreadth.DirectoryCount:N0} dirs/{summary.FirstSideBreadth.FileCount:N0} files  ↔  {summary.SecondSideBreadth.DirectoryCount:N0} dirs/{summary.SecondSideBreadth.FileCount:N0} files");
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
        return evidence.Timing;
    }

    private static void AppendDirectoryPairStats(StringBuilder builder, DirectoryPair pair,
        IReadOnlyDictionary<FileSystemPath, DirectoryRecord> records,
        IReadOnlyDictionary<FileSystemPath, DirectoryGraphNode> nodes, string indent)
    {
        if (!records.TryGetValue(pair.First, out var first) || !records.TryGetValue(pair.Second, out var second)) return;
        var firstCoverage = Ratio(pair.Leverage, first.DuplicateContentCount);
        var secondCoverage = Ratio(pair.Leverage, second.DuplicateContentCount);
        var union = first.DuplicateContentCount + second.DuplicateContentCount - pair.Leverage;
        var jaccard = Ratio(pair.Leverage, union);
        var firstNode = nodes.TryGetValue(pair.First, out var firstGraphNode) ? firstGraphNode : null;
        var secondNode = nodes.TryGetValue(pair.Second, out var secondGraphNode) ? secondGraphNode : null;
        var firstDegree = firstNode?.Degree ?? 0;
        var secondDegree = secondNode?.Degree ?? 0;
        var firstConcentration = firstNode is null ? 0 : Ratio(pair.Leverage, firstNode.WeightedDegree);
        var secondConcentration = secondNode is null ? 0 : Ratio(pair.Leverage, secondNode.WeightedDegree);
        builder.AppendLine($"{indent}shared {pair.Leverage:N0}; coverage {firstCoverage:P1}/{secondCoverage:P1}; Jaccard {jaccard:P1}; degrees {firstDegree:N0}/{secondDegree:N0}; edge concentration {firstConcentration:P1}/{secondConcentration:P1}");
    }

    private static double Ratio(long numerator, long denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
    private static double Reduction(int child, int parent) => parent == 0 ? 0 : 1d - (double)child / parent;
    private static double CoverageAsymmetry(double minimum, double maximum) => maximum == 0 ? 0 : 1d - minimum / maximum;
    private static string Kind(Case item) => item switch { DuplicateSetCase => "DuplicateSet", SingleDirectoryCase => "SingleDirectory", DirectoryPairCase => "DirectoryPair", ScopePairCase => "ScopePair", _ => item.GetType().Name };
    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalSeconds >= 1 ? elapsed.TotalSeconds.ToString("N3") + " s" : elapsed.TotalMilliseconds.ToString("N1") + " ms";
}
