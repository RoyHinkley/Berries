using System.Text;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record DirectoryNamesakeMinHashCandidate(
    int MatchingBands,
    int TotalBands,
    IReadOnlyList<int> Bands,
    IReadOnlyList<DirectoryNamesakeMinHashMember> Members);

public sealed record DirectoryNamesakeMinHashMember(
    FileSystemPath Path,
    int DescendantNamesakeCount,
    int DistinctDescendantNamesakeCount,
    int MaxDescendantNamesakeDepth);

/// <summary>
/// Experimental fuzzy structural comparison of Directory Namesakes.
/// Each Namesake Directory is represented by its own Namesake plus the set of Namesake leaf names beneath it.
/// A MinHash signature approximates Jaccard similarity of those sets; repeated identical LSH member sets
/// across bands are consolidated into candidate collections without an exhaustive pairwise comparison.
/// </summary>
public static class DirectoryNamesakeMinHashAnalyzer
{
    public static IReadOnlyList<DirectoryNamesakeMinHashCandidate> Analyze(
        BerriesSession session,
        IFileSystem fileSystem,
        int signatureLength = 64,
        int rowsPerBand = 4,
        int minimumDescendantNamesakes = 2,
        int resultLimit = 250,
        CancellationToken cancellationToken = default)
    {
        if (signatureLength < 1) throw new ArgumentOutOfRangeException(nameof(signatureLength));
        if (rowsPerBand < 1 || signatureLength % rowsPerBand != 0)
            throw new ArgumentOutOfRangeException(nameof(rowsPerBand), "Rows per band must evenly divide the signature length.");
        if (minimumDescendantNamesakes < 1) throw new ArgumentOutOfRangeException(nameof(minimumDescendantNamesakes));
        if (resultLimit < 1) throw new ArgumentOutOfRangeException(nameof(resultLimit));

        var directories = BuildDirectoryInventory(session, fileSystem, cancellationToken);
        var namesakes = directories
            .Select(path => (Path: path, Name: Path.GetFileName(path.Value)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();

        var namesakeDirectories = namesakes
            .SelectMany(group => group.Select(item => item.Path))
            .Distinct()
            .ToArray();

        var featuresByDirectory = new Dictionary<FileSystemPath, HashSet<string>>();
        var descendantOccurrenceCounts = new Dictionary<FileSystemPath, int>();
        var maxDescendantDepths = new Dictionary<FileSystemPath, int>();

        foreach (var group in namesakes)
        {
            foreach (var occurrence in group.Select(item => item.Path))
            {
                featuresByDirectory[occurrence] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    group.Key
                };
                descendantOccurrenceCounts[occurrence] = 0;
                maxDescendantDepths[occurrence] = 0;
            }
        }

        // Walk each Namesake occurrence upward. Its leaf name becomes a feature of every Namesake ancestor,
        // while occurrence count and maximum structural depth retain information that MinHash itself discards.
        foreach (var group in namesakes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var feature = group.Key;
            foreach (var occurrence in group.Select(item => item.Path))
            {
                var depth = 1;
                var current = fileSystem.GetParentDirectory(occurrence);
                while (current is { } ancestor && InsideCorpus(ancestor, session.Corpus, fileSystem))
                {
                    if (featuresByDirectory.TryGetValue(ancestor, out var features))
                    {
                        features.Add(feature);
                        descendantOccurrenceCounts[ancestor]++;
                        if (depth > maxDescendantDepths[ancestor])
                            maxDescendantDepths[ancestor] = depth;
                    }

                    if (session.Corpus.Roots.Any(root => fileSystem.PathsEqual(ancestor, root.Path)))
                        break;

                    current = fileSystem.GetParentDirectory(ancestor);
                    depth++;
                }
            }
        }

        var bands = signatureLength / rowsPerBand;
        var bandBuckets = new Dictionary<(int Band, ulong Hash), List<DirectoryNamesakeMinHashMember>>();

        foreach (var directory in namesakeDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var descendantCount = descendantOccurrenceCounts[directory];
            if (descendantCount < minimumDescendantNamesakes)
                continue;

            var features = featuresByDirectory[directory];
            var ownName = Path.GetFileName(directory.Value);
            var distinctDescendantCount = features.Count - (features.Contains(ownName) ? 1 : 0);
            var member = new DirectoryNamesakeMinHashMember(
                directory,
                descendantCount,
                Math.Max(0, distinctDescendantCount),
                maxDescendantDepths[directory]);

            var signature = MinHashSignature(features, signatureLength);
            for (var band = 0; band < bands; band++)
            {
                var bandHash = HashBand(signature, band * rowsPerBand, rowsPerBand);
                var key = (band, bandHash);
                if (!bandBuckets.TryGetValue(key, out var members))
                    bandBuckets[key] = members = [];
                members.Add(member);
            }
        }

        // A raw LSH bucket is implementation evidence, not the object we ultimately want to inspect.
        // Consolidate buckets having exactly the same member set; the number of bands producing that set
        // becomes the primary similarity evidence without pairwise Jaccard or transitive clustering.
        var collections = new Dictionary<string, CandidateAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in bandBuckets.Where(item => item.Value.Count > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var members = bucket.Value
                .OrderBy(member => member.Path.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var key = string.Join("\n", members.Select(member => member.Path.Value));

            if (!collections.TryGetValue(key, out var accumulator))
                collections[key] = accumulator = new CandidateAccumulator(members);
            accumulator.Bands.Add(bucket.Key.Band);
        }

        return collections.Values
            .Select(accumulator => new DirectoryNamesakeMinHashCandidate(
                accumulator.Bands.Count,
                bands,
                accumulator.Bands.OrderBy(band => band).ToArray(),
                accumulator.Members))
            .OrderByDescending(candidate => candidate.MatchingBands)
            .ThenByDescending(candidate => candidate.Members.Min(member => member.DescendantNamesakeCount))
            .ThenByDescending(candidate => candidate.Members.Average(member => member.DescendantNamesakeCount))
            .ThenBy(candidate => candidate.Members.Max(member => member.MaxDescendantNamesakeDepth))
            .ThenBy(candidate => candidate.Members.Average(member => member.MaxDescendantNamesakeDepth))
            .ThenByDescending(candidate => candidate.Members.Count)
            .Take(resultLimit)
            .ToArray();
    }

    private sealed class CandidateAccumulator(IReadOnlyList<DirectoryNamesakeMinHashMember> members)
    {
        public IReadOnlyList<DirectoryNamesakeMinHashMember> Members { get; } = members;
        public HashSet<int> Bands { get; } = [];
    }

    private static ulong[] MinHashSignature(IEnumerable<string> features, int signatureLength)
    {
        var signature = Enumerable.Repeat(ulong.MaxValue, signatureLength).ToArray();
        foreach (var feature in features)
        {
            var baseHash = StableStringHash(feature);
            for (var index = 0; index < signature.Length; index++)
            {
                var hash = SplitMix64(baseHash ^ Seed(index));
                if (hash < signature[index])
                    signature[index] = hash;
            }
        }
        return signature;
    }

    private static ulong HashBand(ulong[] signature, int start, int count)
    {
        var hash = 14695981039346656037UL;
        for (var index = start; index < start + count; index++)
        {
            hash ^= signature[index];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static ulong StableStringHash(string value)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(value.ToUpperInvariant()))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static ulong Seed(int index) =>
        0x9E3779B97F4A7C15UL * (ulong)(index + 1);

    private static ulong SplitMix64(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
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
}
