using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>Elementary graph metrics for the Directory Pair network.</summary>
public sealed record DirectoryGraphAnalysis(
    int TotalDirectoryCount,
    int GroupedDirectoryCount,
    int InternalGroupDirectoryCount,
    int PairParticipatingDirectoryCount,
    int DirectoryPairCount,
    int ConnectedComponentCount,
    int LargestComponentSize,
    double PairDensity,
    IReadOnlyList<DirectoryGraphNode> Nodes);

public sealed record DirectoryGraphNode(
    FileSystemPath Directory,
    int Degree,
    int WeightedDegree,
    int MaxSharedGroupCount,
    int FileCount,
    int GroupedFileCount,
    int GroupCount)
{
    /// <summary>Mean number of shared Groups across incident Directory Pairs.</summary>
    public double MeanSharedGroupCount => Degree == 0 ? 0 : (double)WeightedDegree / Degree;

    /// <summary>Fraction of weighted degree represented by the strongest incident Directory Pair.</summary>
    public double StrongestPairConcentration => WeightedDegree == 0 ? 0 : (double)MaxSharedGroupCount / WeightedDegree;
}

internal static class DirectoryGraphAnalyzer
{
    public static DirectoryGraphAnalysis Analyze(
        Portrait portrait,
        IReadOnlyList<DirectoryRecord> directories,
        IReadOnlyList<DirectoryPair> directoryPairs,
        IReadOnlySet<FileSystemPath> internalGroupDirectories)
    {
        var totalDirectoryCount = portrait.Files
            .Select(file => file.ParentDirectory)
            .Distinct()
            .Count();

        var degree = directories.ToDictionary(directory => directory.Path, _ => 0);
        var weightedDegree = directories.ToDictionary(directory => directory.Path, _ => 0);
        var maxSharedGroups = directories.ToDictionary(directory => directory.Path, _ => 0);
        var adjacency = directories.ToDictionary(
            directory => directory.Path,
            _ => new HashSet<FileSystemPath>());

        foreach (var pair in directoryPairs)
        {
            degree[pair.First]++;
            degree[pair.Second]++;
            weightedDegree[pair.First] += pair.SharedGroupCount;
            weightedDegree[pair.Second] += pair.SharedGroupCount;
            maxSharedGroups[pair.First] = Math.Max(maxSharedGroups[pair.First], pair.SharedGroupCount);
            maxSharedGroups[pair.Second] = Math.Max(maxSharedGroups[pair.Second], pair.SharedGroupCount);
            adjacency[pair.First].Add(pair.Second);
            adjacency[pair.Second].Add(pair.First);
        }

        var nodes = directories
            .Select(directory => new DirectoryGraphNode(
                directory.Path,
                degree[directory.Path],
                weightedDegree[directory.Path],
                maxSharedGroups[directory.Path],
                directory.FileCount,
                directory.GroupedFileCount,
                directory.GroupCount))
            .OrderByDescending(node => node.Degree)
            .ThenByDescending(node => node.WeightedDegree)
            .ThenBy(node => node.Directory.Value, StringComparer.Ordinal)
            .ToArray();

        var participating = nodes.Where(node => node.Degree > 0).ToArray();
        var visited = new HashSet<FileSystemPath>();
        var componentCount = 0;
        var largestComponent = 0;

        foreach (var node in nodes)
        {
            if (!visited.Add(node.Directory))
                continue;

            componentCount++;
            var componentSize = 0;
            var pending = new Stack<FileSystemPath>();
            pending.Push(node.Directory);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                componentSize++;

                foreach (var neighbor in adjacency[current])
                {
                    if (visited.Add(neighbor))
                        pending.Push(neighbor);
                }
            }

            largestComponent = Math.Max(largestComponent, componentSize);
        }

        var possiblePairs = participating.Length < 2
            ? 0d
            : (double)participating.Length * (participating.Length - 1) / 2d;
        var density = possiblePairs == 0 ? 0 : directoryPairs.Count / possiblePairs;

        return new DirectoryGraphAnalysis(
            totalDirectoryCount,
            directories.Count,
            internalGroupDirectories.Count,
            participating.Length,
            directoryPairs.Count,
            componentCount,
            largestComponent,
            density,
            nodes);
    }
}
