using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record DirectoryNamesakeStructureCandidate(
    IReadOnlyList<FileSystemPath> Branches,
    IReadOnlyList<DirectoryNamesakeEvidence> SharedNamesakes,
    double Score);

public sealed record DirectoryNamesakeEvidence(
    string Name,
    int CorpusDirectoryCount);

/// <summary>
/// Finds collections of non-nested Branches that contain unusually similar sets of
/// recurring Directory names. Candidate generation uses pairs drawn from each Branch's
/// few rarest Namesakes rather than enumerating all Branch pairs.
/// </summary>
public static class DirectoryNamesakeStructureAnalyzer
{
    public static IReadOnlyList<DirectoryNamesakeStructureCandidate> Analyze(
        BerriesSession session,
        IFileSystem fileSystem,
        int anchorCount = 8,
        int minimumSharedNamesakes = 4,
        int resultLimit = 50,
        CancellationToken cancellationToken = default)
    {
        if (anchorCount < 2) throw new ArgumentOutOfRangeException(nameof(anchorCount));
        if (minimumSharedNamesakes < 2) throw new ArgumentOutOfRangeException(nameof(minimumSharedNamesakes));
        if (resultLimit < 1) throw new ArgumentOutOfRangeException(nameof(resultLimit));

        var directories = BuildDirectoryInventory(session, fileSystem, cancellationToken);
        var namesakes = directories
            .Select(path => (Path: path, Name: Path.GetFileName(path.Value)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Path).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var frequencies = namesakes.ToDictionary(
            item => item.Key,
            item => item.Value.Length,
            StringComparer.OrdinalIgnoreCase);

        var featuresByBranch = new Dictionary<FileSystemPath, HashSet<string>>();
        foreach (var namesake in namesakes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var occurrence in namesake.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddFeatureToAncestors(
                    occurrence,
                    namesake.Key,
                    session.Corpus,
                    fileSystem,
                    featuresByBranch);
            }
        }

        var buckets = new Dictionary<string, HashSet<FileSystemPath>>(StringComparer.OrdinalIgnoreCase);
        foreach (var branch in featuresByBranch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (branch.Value.Count < minimumSharedNamesakes)
                continue;

            var anchors = branch.Value
                .OrderBy(name => frequencies[name])
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(anchorCount)
                .ToArray();

            for (var first = 0; first < anchors.Length - 1; first++)
            {
                for (var second = first + 1; second < anchors.Length; second++)
                {
                    var key = AnchorKey(anchors[first], anchors[second]);
                    if (!buckets.TryGetValue(key, out var members))
                        buckets[key] = members = [];
                    members.Add(branch.Key);
                }
            }
        }

        var candidates = new Dictionary<string, DirectoryNamesakeStructureCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in buckets.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bucket.Count < 2)
                continue;

            var branches = RemoveAncestorDuplicates(bucket, fileSystem);
            if (branches.Count < 2)
                continue;

            HashSet<string>? shared = null;
            foreach (var branch in branches)
            {
                if (!featuresByBranch.TryGetValue(branch, out var features))
                    continue;
                if (shared is null)
                    shared = new HashSet<string>(features, StringComparer.OrdinalIgnoreCase);
                else
                    shared.IntersectWith(features);
                if (shared.Count < minimumSharedNamesakes)
                    break;
            }

            if (shared is null || shared.Count < minimumSharedNamesakes)
                continue;

            var evidence = shared
                .Select(name => new DirectoryNamesakeEvidence(name, frequencies[name]))
                .OrderBy(item => item.CorpusDirectoryCount)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var score = evidence.Sum(item => Math.Log((directories.Count + 1.0) / item.CorpusDirectoryCount))
                * Math.Log2(branches.Count + 1.0);

            var candidate = new DirectoryNamesakeStructureCandidate(branches, evidence, score);
            var candidateKey = string.Join(
                "\u001e",
                branches.Select(path => path.Value)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

            if (!candidates.TryGetValue(candidateKey, out var existing) || candidate.Score > existing.Score)
                candidates[candidateKey] = candidate;
        }

        return candidates.Values
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.SharedNamesakes.Count)
            .ThenByDescending(candidate => candidate.Branches.Count)
            .Take(resultLimit)
            .ToArray();
    }

    private static HashSet<FileSystemPath> BuildDirectoryInventory(
        BerriesSession session,
        IFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var directories = new HashSet<FileSystemPath>();
        foreach (var root in session.Corpus.Roots)
            directories.Add(root.Path);

        foreach (var directory in session.UniqueFileCountsByDirectory.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddAncestorsWithinCorpus(directory, session.Corpus, fileSystem, directories);
        }

        foreach (var directory in session.WorkingPortrait.Files.Select(file => file.ParentDirectory).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddAncestorsWithinCorpus(directory, session.Corpus, fileSystem, directories);
        }

        return directories;
    }

    private static void AddFeatureToAncestors(
        FileSystemPath occurrence,
        string feature,
        Corpus corpus,
        IFileSystem fileSystem,
        IDictionary<FileSystemPath, HashSet<string>> featuresByBranch)
    {
        var current = occurrence;
        while (true)
        {
            if (!featuresByBranch.TryGetValue(current, out var features))
                featuresByBranch[current] = features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            features.Add(feature);

            if (corpus.Roots.Any(root => fileSystem.PathsEqual(current, root.Path)))
                return;

            var parent = fileSystem.GetParentDirectory(current);
            if (parent is null || !InsideCorpus(parent.Value, corpus, fileSystem))
                return;
            current = parent.Value;
        }
    }

    private static void AddAncestorsWithinCorpus(
        FileSystemPath directory,
        Corpus corpus,
        IFileSystem fileSystem,
        ISet<FileSystemPath> directories)
    {
        var current = directory;
        while (true)
        {
            directories.Add(current);
            if (corpus.Roots.Any(root => fileSystem.PathsEqual(current, root.Path)))
                return;

            var parent = fileSystem.GetParentDirectory(current);
            if (parent is null || !InsideCorpus(parent.Value, corpus, fileSystem))
                return;
            current = parent.Value;
        }
    }

    private static bool InsideCorpus(FileSystemPath path, Corpus corpus, IFileSystem fileSystem) =>
        corpus.Roots.Any(root =>
            fileSystem.PathsEqual(path, root.Path)
            || fileSystem.IsDescendant(path, root.Path));

    private static IReadOnlyList<FileSystemPath> RemoveAncestorDuplicates(
        IEnumerable<FileSystemPath> branches,
        IFileSystem fileSystem)
    {
        var deepestFirst = branches
            .OrderByDescending(path => path.Value.Length)
            .ThenBy(path => path.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new List<FileSystemPath>();

        foreach (var candidate in deepestFirst)
        {
            if (result.Any(deeper => fileSystem.IsDescendant(deeper, candidate)))
                continue;
            result.Add(candidate);
        }

        return result
            .OrderBy(path => path.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string AnchorKey(string first, string second) =>
        StringComparer.OrdinalIgnoreCase.Compare(first, second) <= 0
            ? $"{first}\u001f{second}"
            : $"{second}\u001f{first}";
}
