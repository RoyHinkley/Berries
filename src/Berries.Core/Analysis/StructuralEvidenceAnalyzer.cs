using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record ScopePairEvidence(
    bool RootsNested,
    int FirstSideDuplicateContentCount,
    int SecondSideDuplicateContentCount,
    int EffectiveDirectoryCount,
    int EffectiveFileCount,
    int SubsidiaryScopePairCount,
    IReadOnlyList<ScopePairEvidenceSummary> StrongestSubsidiaryScopePairs,
    IReadOnlyList<DirectoryPair> StrongestContributingDirectoryPairs);

public sealed record ScopePairEvidenceSummary(
    ScopePair Pair,
    int EffectiveDirectoryCount,
    int EffectiveFileCount);

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
        var contributingDirectoryPairs = directoryPairs
            .Where(candidate => CrossesEffectiveSides(
                candidate.First,
                candidate.Second,
                pair.FirstRoot,
                pair.SecondRoot))
            .OrderByDescending(candidate => candidate.Leverage)
            .ThenBy(candidate => candidate.First.Value, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Second.Value, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        var subsidiaries = scopePairs
            .Where(candidate => IsStrictSubsidiary(candidate, pair))
            .OrderByDescending(candidate => candidate.Leverage)
            .ThenByDescending(candidate => candidate.DirectoryPairCount)
            .ThenBy(candidate => candidate.FirstRoot.Value, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SecondRoot.Value, StringComparer.Ordinal)
            .ToArray();

        var firstSideContentCount = duplicateSets.Count(set =>
            set.Files.Any(file => IsInEffectiveSide(
                file.ParentDirectory,
                pair.FirstRoot,
                pair.SecondRoot)));
        var secondSideContentCount = duplicateSets.Count(set =>
            set.Files.Any(file => IsInEffectiveSide(
                file.ParentDirectory,
                pair.SecondRoot,
                pair.FirstRoot)));

        var breadth = GetBreadth(pair, portraitFiles);
        var strongestSubsidiaries = subsidiaries
            .Take(limit)
            .Select(candidate =>
            {
                var candidateBreadth = GetBreadth(candidate, portraitFiles);
                return new ScopePairEvidenceSummary(
                    candidate,
                    candidateBreadth.DirectoryCount,
                    candidateBreadth.FileCount);
            })
            .ToArray();

        return new ScopePairEvidence(
            fileSystem.IsDescendant(pair.FirstRoot, pair.SecondRoot)
                || fileSystem.IsDescendant(pair.SecondRoot, pair.FirstRoot),
            firstSideContentCount,
            secondSideContentCount,
            breadth.DirectoryCount,
            breadth.FileCount,
            subsidiaries.Length,
            strongestSubsidiaries,
            contributingDirectoryPairs);
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

    private (int DirectoryCount, int FileCount) GetBreadth(
        ScopePair pair,
        IReadOnlyList<FileInstance> portraitFiles)
    {
        var directories = new HashSet<FileSystemPath>();
        var fileCount = 0;

        foreach (var file in portraitFiles)
        {
            if (!IsInEffectiveSide(file.ParentDirectory, pair.FirstRoot, pair.SecondRoot)
                && !IsInEffectiveSide(file.ParentDirectory, pair.SecondRoot, pair.FirstRoot))
                continue;

            fileCount++;
            directories.Add(file.ParentDirectory);
        }

        return (directories.Count, fileCount);
    }

    private bool SameUnorderedPair(ScopePair first, ScopePair second) =>
        (fileSystem.PathsEqual(first.FirstRoot, second.FirstRoot)
            && fileSystem.PathsEqual(first.SecondRoot, second.SecondRoot))
        || (fileSystem.PathsEqual(first.FirstRoot, second.SecondRoot)
            && fileSystem.PathsEqual(first.SecondRoot, second.FirstRoot));

    private bool CrossesEffectiveSides(
        FileSystemPath firstDirectory,
        FileSystemPath secondDirectory,
        FileSystemPath firstRoot,
        FileSystemPath secondRoot) =>
        (IsInEffectiveSide(firstDirectory, firstRoot, secondRoot)
            && IsInEffectiveSide(secondDirectory, secondRoot, firstRoot))
        || (IsInEffectiveSide(secondDirectory, firstRoot, secondRoot)
            && IsInEffectiveSide(firstDirectory, secondRoot, firstRoot));

    private bool IsInEffectiveSide(
        FileSystemPath directory,
        FileSystemPath ownRoot,
        FileSystemPath otherRoot)
    {
        if (!Contains(ownRoot, directory))
            return false;

        if (fileSystem.IsDescendant(otherRoot, ownRoot) && Contains(otherRoot, directory))
            return false;

        return true;
    }

    private bool Contains(FileSystemPath root, FileSystemPath path) =>
        fileSystem.PathsEqual(root, path) || fileSystem.IsDescendant(path, root);
}
