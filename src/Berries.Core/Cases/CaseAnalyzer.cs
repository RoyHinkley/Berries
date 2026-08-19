using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Cases;

/// <summary>Constructs the objective Case population from one analyzed Portrait.</summary>
public sealed class CaseAnalyzer(IFileSystem fileSystem)
{
    public CaseAnalysisResult Analyze(
        Portrait portrait,
        IReadOnlyList<DuplicateSet> duplicateSets,
        IReadOnlyList<DirectoryPair> directoryPairs,
        IReadOnlyList<ScopePair> scopePairs)
    {
        var cases = new List<Case>();

        cases.AddRange(duplicateSets.Select(set => new DuplicateSetCase(set)));

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

        foreach (var item in internalDuplicateContents)
        {
            var boundedFiles = portrait.Files
                .Where(file => fileSystem.PathsEqual(file.ParentDirectory, item.Key))
                .ToArray();
            cases.Add(new SingleDirectoryCase(item.Key, boundedFiles, item.Value.Count));
        }

        foreach (var pair in directoryPairs)
        {
            var boundedFiles = portrait.Files
                .Where(file =>
                    fileSystem.PathsEqual(file.ParentDirectory, pair.First)
                    || fileSystem.PathsEqual(file.ParentDirectory, pair.Second))
                .ToArray();
            cases.Add(new DirectoryPairCase(pair, boundedFiles));
        }

        foreach (var pair in scopePairs)
        {
            var boundedFiles = portrait.Files
                .Where(file =>
                    IsInEffectiveSide(file.ParentDirectory, pair.FirstRoot, pair.SecondRoot)
                    || IsInEffectiveSide(file.ParentDirectory, pair.SecondRoot, pair.FirstRoot))
                .ToArray();
            cases.Add(new ScopePairCase(pair, boundedFiles));
        }

        var ordered = cases
            .OrderByDescending(item => item.Leverage)
            .ThenBy(item => item.GetType().Name, StringComparer.Ordinal)
            .ThenBy(CaseKey, StringComparer.Ordinal)
            .ToArray();

        return new CaseAnalysisResult(ordered);
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

    private static string CaseKey(Case item) => item switch
    {
        DuplicateSetCase duplicate => duplicate.DuplicateSet.Content.Value,
        SingleDirectoryCase directory => directory.Directory.Value,
        DirectoryPairCase pair => pair.Pair.First.Value + "\n" + pair.Pair.Second.Value,
        ScopePairCase pair => pair.Pair.FirstRoot.Value + "\n" + pair.Pair.SecondRoot.Value,
        _ => string.Empty
    };
}
