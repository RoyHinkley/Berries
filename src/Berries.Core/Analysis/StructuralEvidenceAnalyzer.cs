using System.Diagnostics;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record ScopeSideBreadth(
    int DirectoryCount,
    int FileCount,
    int CrossingDirectoryCount);

public sealed record ScopePairEvidenceTiming(
    TimeSpan ContributingDirectoryPairs,
    TimeSpan SubsidiaryScopePairs,
    TimeSpan ParentBreadth,
    TimeSpan SubsidiaryBreadth,
    TimeSpan Total);

public sealed record ScopePairEvidence(
    bool RootsNested,
    int FirstSideDuplicateContentCount,
    int SecondSideDuplicateContentCount,
    ScopeSideBreadth FirstSideBreadth,
    ScopeSideBreadth SecondSideBreadth,
    int SubsidiaryScopePairCount,
    IReadOnlyList<ScopePairEvidenceSummary> StrongestSubsidiaryScopePairs,
    IReadOnlyList<DirectoryPair> StrongestContributingDirectoryPairs,
    double StrongestDirectoryPairFraction,
    double TopFiveDirectoryPairFraction,
    double TopTenDirectoryPairFraction,
    ScopePairEvidenceTiming Timing);

public sealed record ScopePairEvidenceSummary(
    ScopePair Pair,
    ScopeSideBreadth FirstSideBreadth,
    ScopeSideBreadth SecondSideBreadth,
    int FirstRootDepthChange,
    int SecondRootDepthChange);

/// <summary>
/// Computes inexpensive objective evidence for sampled structural cases without enlarging
/// the persistent ScopePair graph.
/// </summary>
public sealed class StructuralEvidenceAnalyzer(IFileSystem fileSystem)
{
    public ScopePairEvidence AnalyzeScopePair(
        ScopePair pair,
        IReadOnlyList<FileInstance> portraitFiles,
        IReadOnlyList<DuplicateSet> duplicateSets,
        IReadOnlyList<DirectoryPair> directoryPairs,
        IReadOnlyList<ScopePair> scopePairs,
        int limit = 10)
    {
        var totalTimer = Stopwatch.StartNew();
        var phaseTimer = Stopwatch.StartNew();

        var contributing = new List<DirectoryPair>();
        var firstCrossingDirectories = new HashSet<FileSystemPath>();
        var secondCrossingDirectories = new HashSet<FileSystemPath>();

        foreach (var candidate in directoryPairs)
        {
            if (!TryOrientAcrossEffectiveSides(candidate.First, candidate.Second, pair, out var firstDirectory, out var secondDirectory))
                continue;

            firstCrossingDirectories.Add(firstDirectory);
            secondCrossingDirectories.Add(secondDirectory);
            InsertStrongest(contributing, candidate, limit, CompareDirectoryPairs);
        }

        phaseTimer.Stop();
        var contributingElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        var strongestSubsidiaries = new List<ScopePair>();
        var subsidiaryCount = 0;
        foreach (var candidate in scopePairs)
        {
            if (!IsStrictSubsidiary(candidate, pair))
                continue;

            subsidiaryCount++;
            InsertStrongest(strongestSubsidiaries, candidate, limit, CompareScopePairs);
        }
        phaseTimer.Stop();
        var subsidiaryElapsed = phaseTimer.Elapsed;

        var firstSideContentCount = duplicateSets.Count(set =>
            set.Files.Any(file => IsInEffectiveSide(file.ParentDirectory, pair.FirstRoot, pair.SecondRoot)));
        var secondSideContentCount = duplicateSets.Count(set =>
            set.Files.Any(file => IsInEffectiveSide(file.ParentDirectory, pair.SecondRoot, pair.FirstRoot)));

        phaseTimer.Restart();
        var parentBreadth = GetBreadth(pair, portraitFiles, firstCrossingDirectories, secondCrossingDirectories);
        phaseTimer.Stop();
        var parentBreadthElapsed = phaseTimer.Elapsed;

        phaseTimer.Restart();
        var subsidiarySummaries = strongestSubsidiaries
            .Select(candidate =>
            {
                var candidateBreadth = GetBreadth(candidate, portraitFiles, null, null);
                var oriented = OrientSubsidiary(candidate, pair);
                return new ScopePairEvidenceSummary(
                    candidate,
                    candidateBreadth.First,
                    candidateBreadth.Second,
                    DirectoryDepthChange(oriented.FirstRoot, pair.FirstRoot),
                    DirectoryDepthChange(oriented.SecondRoot, pair.SecondRoot));
            })
            .ToArray();
        phaseTimer.Stop();
        var subsidiaryBreadthElapsed = phaseTimer.Elapsed;

        totalTimer.Stop();
        var leverage = pair.Leverage;
        return new ScopePairEvidence(
            fileSystem.IsDescendant(pair.FirstRoot, pair.SecondRoot)
                || fileSystem.IsDescendant(pair.SecondRoot, pair.FirstRoot),
            firstSideContentCount,
            secondSideContentCount,
            parentBreadth.First,
            parentBreadth.Second,
            subsidiaryCount,
            subsidiarySummaries,
            contributing,
            Ratio(contributing.Take(1).Sum(item => item.Leverage), leverage),
            Ratio(contributing.Take(5).Sum(item => item.Leverage), leverage),
            Ratio(contributing.Take(10).Sum(item => item.Leverage), leverage),
            new ScopePairEvidenceTiming(
                contributingElapsed,
                subsidiaryElapsed,
                parentBreadthElapsed,
                subsidiaryBreadthElapsed,
                totalTimer.Elapsed));
    }

    public bool IsStrictSubsidiary(ScopePair candidate, ScopePair parent)
    {
        if (SameUnorderedPair(candidate, parent))
            return false;

        return (IsInEffectiveSide(candidate.FirstRoot, parent.FirstRoot, parent.SecondRoot)
                && IsInEffectiveSide(candidate.SecondRoot, parent.SecondRoot, parent.FirstRoot))
            || (IsInEffectiveSide(candidate.SecondRoot, parent.FirstRoot, parent.SecondRoot)
                && IsInEffectiveSide(candidate.FirstRoot, parent.SecondRoot, parent.FirstRoot));
    }

    private (ScopeSideBreadth First, ScopeSideBreadth Second) GetBreadth(
        ScopePair pair,
        IReadOnlyList<FileInstance> portraitFiles,
        IReadOnlySet<FileSystemPath>? firstCrossingDirectories,
        IReadOnlySet<FileSystemPath>? secondCrossingDirectories)
    {
        var firstDirectories = new HashSet<FileSystemPath>();
        var secondDirectories = new HashSet<FileSystemPath>();
        var firstFiles = 0;
        var secondFiles = 0;

        foreach (var file in portraitFiles)
        {
            if (IsInEffectiveSide(file.ParentDirectory, pair.FirstRoot, pair.SecondRoot))
            {
                firstFiles++;
                firstDirectories.Add(file.ParentDirectory);
            }
            else if (IsInEffectiveSide(file.ParentDirectory, pair.SecondRoot, pair.FirstRoot))
            {
                secondFiles++;
                secondDirectories.Add(file.ParentDirectory);
            }
        }

        return (
            new ScopeSideBreadth(firstDirectories.Count, firstFiles, firstCrossingDirectories?.Count ?? 0),
            new ScopeSideBreadth(secondDirectories.Count, secondFiles, secondCrossingDirectories?.Count ?? 0));
    }

    private (FileSystemPath FirstRoot, FileSystemPath SecondRoot) OrientSubsidiary(ScopePair candidate, ScopePair parent)
    {
        if (IsInEffectiveSide(candidate.FirstRoot, parent.FirstRoot, parent.SecondRoot)
            && IsInEffectiveSide(candidate.SecondRoot, parent.SecondRoot, parent.FirstRoot))
            return (candidate.FirstRoot, candidate.SecondRoot);

        return (candidate.SecondRoot, candidate.FirstRoot);
    }

    private int DirectoryDepthChange(FileSystemPath descendant, FileSystemPath ancestor)
    {
        if (fileSystem.PathsEqual(descendant, ancestor))
            return 0;

        var depth = 0;
        FileSystemPath? current = descendant;
        while (current is not null && !fileSystem.PathsEqual(current.Value, ancestor))
        {
            current = fileSystem.GetParentDirectory(current.Value);
            depth++;
        }

        return current is null ? 0 : depth;
    }

    private bool SameUnorderedPair(ScopePair first, ScopePair second) =>
        (fileSystem.PathsEqual(first.FirstRoot, second.FirstRoot)
            && fileSystem.PathsEqual(first.SecondRoot, second.SecondRoot))
        || (fileSystem.PathsEqual(first.FirstRoot, second.SecondRoot)
            && fileSystem.PathsEqual(first.SecondRoot, second.FirstRoot));

    private bool TryOrientAcrossEffectiveSides(
        FileSystemPath firstDirectory,
        FileSystemPath secondDirectory,
        ScopePair pair,
        out FileSystemPath firstSideDirectory,
        out FileSystemPath secondSideDirectory)
    {
        if (IsInEffectiveSide(firstDirectory, pair.FirstRoot, pair.SecondRoot)
            && IsInEffectiveSide(secondDirectory, pair.SecondRoot, pair.FirstRoot))
        {
            firstSideDirectory = firstDirectory;
            secondSideDirectory = secondDirectory;
            return true;
        }

        if (IsInEffectiveSide(secondDirectory, pair.FirstRoot, pair.SecondRoot)
            && IsInEffectiveSide(firstDirectory, pair.SecondRoot, pair.FirstRoot))
        {
            firstSideDirectory = secondDirectory;
            secondSideDirectory = firstDirectory;
            return true;
        }

        firstSideDirectory = default;
        secondSideDirectory = default;
        return false;
    }

    private bool IsInEffectiveSide(FileSystemPath directory, FileSystemPath ownRoot, FileSystemPath otherRoot)
    {
        if (!Contains(ownRoot, directory))
            return false;
        if (fileSystem.IsDescendant(otherRoot, ownRoot) && Contains(otherRoot, directory))
            return false;
        return true;
    }

    private bool Contains(FileSystemPath root, FileSystemPath path) =>
        fileSystem.PathsEqual(root, path) || fileSystem.IsDescendant(path, root);

    private static void InsertStrongest<T>(List<T> items, T item, int limit, Comparison<T> comparison)
    {
        if (limit <= 0)
            return;

        var index = items.BinarySearch(item, Comparer<T>.Create(comparison));
        if (index < 0)
            index = ~index;
        items.Insert(index, item);
        if (items.Count > limit)
            items.RemoveAt(items.Count - 1);
    }

    private static int CompareDirectoryPairs(DirectoryPair first, DirectoryPair second)
    {
        var result = second.Leverage.CompareTo(first.Leverage);
        if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(first.First.Value, second.First.Value);
        return result != 0 ? result : StringComparer.Ordinal.Compare(first.Second.Value, second.Second.Value);
    }

    private static int CompareScopePairs(ScopePair first, ScopePair second)
    {
        var result = second.Leverage.CompareTo(first.Leverage);
        if (result != 0) return result;
        result = second.DirectoryPairCount.CompareTo(first.DirectoryPairCount);
        if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(first.FirstRoot.Value, second.FirstRoot.Value);
        return result != 0 ? result : StringComparer.Ordinal.Compare(first.SecondRoot.Value, second.SecondRoot.Value);
    }

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : (double)numerator / denominator;
}
