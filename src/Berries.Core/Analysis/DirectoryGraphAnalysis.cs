using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>Elementary graph metrics for the DirectoryPair network.</summary>
public sealed record DirectoryGraphAnalysis(
    int TotalDirectoryCount,
    int DuplicateDirectoryCount,
    int InternalDuplicateDirectoryCount,
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
    int MaxPairLeverage,
    int FileCount,
    int DuplicateFileCount,
    int DuplicateContentCount)
{
    /// <summary>Mean leverage of incident DirectoryPairs. High degree with a low mean indicates diffuse sharing.</summary>
    public double MeanPairLeverage => Degree == 0 ? 0 : (double)WeightedDegree / Degree;

    /// <summary>Fraction of weighted degree represented by the strongest incident DirectoryPair.</summary>
    public double StrongestPairConcentration => WeightedDegree == 0 ? 0 : (double)MaxPairLeverage / WeightedDegree;
}

internal static class DirectoryGraphAnalyzer
{
    public static DirectoryGraphAnalysis Analyze(
        Portrait portrait,
        IReadOnlyList<DirectoryRecord> directories,
        IReadOnlyList<DirectoryPair> directoryPairs,
        IReadOnlySet<FileSystemPath> internalDuplicateDirectories)
    {
        var totalDirectoryCount = portrait.Files
            .Select(file => file.ParentDirectory)
            .Distinct()
            .Count();

        var degree = directories.ToDictionary(directory => directory.Path, _ => 0);
        var weightedDegree = directories.ToDictionary(directory => directory.Path, _ => 0);
        var maxLeverage = directories.ToDictionary(directory => directory.Path, _ => 0);
        var adjacency = directories.ToDictionary(
            directory => directory.Path,
            _ => new HashSet<FileSystemPath>());

        foreach (var pair in directoryPairs)
        {
            degree[pair.First]++;
            degree[pair.Second]++;
            weightedDegree[pair.First] += pair.Leverage;
            weightedDegree[pair.Second] += pair.Leverage;
            maxLeverage[pair.First] = Math.Max(maxLeverage[pair.First], pair.Leverage);
            maxLeverage[pair.Second] = Math.Max(maxLeverage[pair.Second], pair.Leverage);
            adjacency[pair.First].Add(pair.Second);
            adjacency[pair.Second].Add(pair.First);
        }

        var nodes = directories
            .Select(directory => new DirectoryGraphNode(
                directory.Path,
                degree[directory.Path],
                weightedDegree[directory.Path],
                maxLeverage[directory.Path],
                directory.FileCount,
                directory.DuplicateFileCount,
                directory.DuplicateContentCount))
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
            internalDuplicateDirectories.Count,
            participating.Length,
            directoryPairs.Count,
            componentCount,
            largestComponent,
            density,
            nodes);
    }
}
