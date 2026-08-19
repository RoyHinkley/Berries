using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Cases;

/// <summary>Ranks the objective Case population and materializes only the requested sample.</summary>
public sealed class CaseAnalyzer(IFileSystem fileSystem)
{
    public CaseAnalysisResult AnalyzeTop(
        Portrait portrait,
        IReadOnlyList<DuplicateSet> duplicateSets,
        IReadOnlyList<DirectoryPair> directoryPairs,
        IReadOnlyList<ScopePair> scopePairs,
        int limit = 25)
    {
        if (limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var internalDuplicateContents = new Dictionary<FileSystemPath, HashSet<ContentId>>();
        foreach (var duplicateSet in duplicateSets)
        {
            foreach (var directoryGroup in duplicateSet.Files.GroupBy(file => file.ParentDirectory))
            {
                if (directoryGroup.Count() < 2)
                    continue;

                if (!internalDuplicateContents.TryGetValue(directoryGroup.Key, out var contents))
                {
                    contents = [];
                    internalDuplicateContents[directoryGroup.Key] = contents;
                }

                contents.Add(duplicateSet.Content);
            }
        }

        var candidates = new List<CaseCandidate>(
            duplicateSets.Count + internalDuplicateContents.Count + directoryPairs.Count + scopePairs.Count);

        candidates.AddRange(duplicateSets.Select(set =>
            new CaseCandidate(1, "DuplicateSet", set.Content.Value, () => new DuplicateSetCase(set))));

        candidates.AddRange(internalDuplicateContents.Select(item =>
            new CaseCandidate(item.Value.Count, "SingleDirectory", item.Key.Value, () =>
                new SingleDirectoryCase(
                    item.Key,
                    portrait.Files.Where(file => fileSystem.PathsEqual(file.ParentDirectory, item.Key)).ToArray(),
                    item.Value.Count))));

        candidates.AddRange(directoryPairs.Select(pair =>
            new CaseCandidate(pair.Leverage, "DirectoryPair", pair.First.Value + "\n" + pair.Second.Value, () =>
                new DirectoryPairCase(
                    pair,
                    portrait.Files.Where(file =>
                        fileSystem.PathsEqual(file.ParentDirectory, pair.First)
                        || fileSystem.PathsEqual(file.ParentDirectory, pair.Second)).ToArray()))));

        candidates.AddRange(scopePairs.Select(pair =>
            new CaseCandidate(pair.Leverage, "ScopePair", pair.FirstRoot.Value + "\n" + pair.SecondRoot.Value, () =>
                new ScopePairCase(
                    pair,
                    portrait.Files.Where(file =>
                        IsInEffectiveSide(file.ParentDirectory, pair.FirstRoot, pair.SecondRoot)
                        || IsInEffectiveSide(file.ParentDirectory, pair.SecondRoot, pair.FirstRoot)).ToArray()))));

        var topCases = candidates
            .OrderByDescending(item => item.Leverage)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(item => item.Materialize())
            .ToArray();

        return new CaseAnalysisResult(
            topCases,
            candidates.Count,
            duplicateSets.Count,
            internalDuplicateContents.Count,
            directoryPairs.Count,
            scopePairs.Count);
    }

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

    private sealed record CaseCandidate(
        int Leverage,
        string Kind,
        string Key,
        Func<Case> Materialize);
}
