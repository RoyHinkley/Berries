using System.Text;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record DirectoryNamesakeMinHashAnalysis(
    IReadOnlyList<DirectoryNamesakeMinHashCandidate> RankedCandidates,
    IReadOnlyList<DirectoryNamesakeMinHashCandidate> Candidates);

public sealed record DirectoryNamesakeMinHashCandidate(
    string Namesake,
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
/// Experimental fuzzy structural comparison within Directory Namesake sets.
/// The common directory name defines the population and is not part of the MinHash feature set.
/// Each occurrence is represented only by the set of Namesake leaf names beneath it.
/// A MinHash signature approximates Jaccard similarity of those descendant sets; repeated identical
/// LSH member sets across bands are consolidated into candidate collections without exhaustive pairwise comparison.
/// Ranked candidates are then culled when their complete member collection lies beneath a stronger candidate.
/// </summary>
public static class DirectoryNamesakeMinHashAnalyzer
{
    public static DirectoryNamesakeMinHashAnalysis Analyze(
        BerriesSession session,
        IFileSystem fileSystem,
        int signatureLength = 64,
        int rowsPerBand = 4,
        int minimumDescendantNamesakes = 2,
        CancellationToken cancellationToken = default)
    {
        if (signatureLength < 1) throw new ArgumentOutOfRangeException(nameof(signatureLength));
        if (rowsPerBand < 1 || signatureLength % rowsPerBand != 0)
            throw new ArgumentOutOfRangeException(nameof(rowsPerBand), "Rows per band must evenly divide the signature length.");
        if (minimumDescendantNamesakes < 1) throw new ArgumentOutOfRangeException(nameof(minimumDescendantNamesakes));

        var directories = BuildDirectoryInventory(session, fileSystem, cancellationToken);
        var namesakes = directories
            .Select(path => (Path: path, Name: Path.GetFileName(path.Value)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();

        var namesakeByDirectory = namesakes
            .SelectMany(group => group.Select(item => (item.Path, Name: group.Key)))
            .ToDictionary(item => item.Path, item => item.Name);

        var featuresByDirectory = new Dictionary<FileSystemPath, HashSet<string>>();
        var descendantOccurrenceCounts = new Dictionary<FileSystemPath, int>();
        var maxDescendantDepths = new Dictionary<FileSystemPath, int>();

        foreach (var directory in namesakeByDirectory.Keys)
        {
            featuresByDirectory[directory] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            descendantOccurrenceCounts[directory] = 0;
            maxDescendantDepths[directory] = 0;
        }

        // Walk each Namesake occurrence upward. Its leaf name becomes a feature of every Namesake ancestor,
        // while occurrence count and maximum structural depth retain information that MinHash itself discards.
        // The ancestor's own Namesake is not added merely because it is the ancestor; same-name membership is
        // enforced separately when LSH buckets are formed.
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
        var bandBuckets = new Dictionary<(string Namesake, int Band, ulong Hash), List<DirectoryNamesakeMinHashMember>>();

        foreach (var (directory, namesake) in namesakeByDirectory)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var descendantCount = descendantOccurrenceCounts[directory];
            if (descendantCount < minimumDescendantNamesakes)
                continue;

            var features = featuresByDirectory[directory];
            var member = new DirectoryNamesakeMinHashMember(
                directory,
                descendantCount,
                features.Count,
                maxDescendantDepths[directory]);

            var signature = MinHashSignature(features, signatureLength);
            for (var band = 0; band < bands; band++)
            {
                var bandHash = HashBand(signature, band * rowsPerBand, rowsPerBand);
                var key = (namesake.ToUpperInvariant(), band, bandHash);
                if (!bandBuckets.TryGetValue(key, out var members))
                    bandBuckets[key] = members = [];
                members.Add(member);
            }
        }

        // A raw LSH bucket is implementation evidence, not the object we ultimately want to inspect.
        // Buckets can contain only occurrences of the same Namesake. Consolidate buckets having exactly
        // the same member set; the number of bands producing that set becomes the primary similarity evidence.
        var collections = new Dictionary<string, CandidateAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in bandBuckets.Where(item => item.Value.Count > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var members = bucket.Value
                .OrderBy(member => member.Path.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var key = bucket.Key.Namesake + "\n" + string.Join("\n", members.Select(member => member.Path.Value));

            if (!collections.TryGetValue(key, out var accumulator))
                collections[key] = accumulator = new CandidateAccumulator(bucket.Key.Namesake, members);
            accumulator.Bands.Add(bucket.Key.Band);
        }

        var ranked = collections.Values
            .Select(accumulator => new DirectoryNamesakeMinHashCandidate(
                accumulator.Namesake,
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
            .ToArray();

        var retained = new List<DirectoryNamesakeMinHashCandidate>();
        foreach (var candidate in ranked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (retained.Any(stronger => IsContainedBy(candidate, stronger, fileSystem)))
                continue;
            retained.Add(candidate);
        }

        return new DirectoryNamesakeMinHashAnalysis(ranked, retained);
    }

    private sealed class CandidateAccumulator(
        string namesake,
        IReadOnlyList<DirectoryNamesakeMinHashMember> members)
    {
        public string Namesake { get; } = namesake;
        public IReadOnlyList<DirectoryNamesakeMinHashMember> Members { get; } = members;
        public HashSet<int> Bands { get; } = [];
    }

    private static bool IsContainedBy(
        DirectoryNamesakeMinHashCandidate candidate,
        DirectoryNamesakeMinHashCandidate stronger,
        IFileSystem fileSystem)
    {
        var strictlyContained = false;

        foreach (var member in candidate.Members)
        {
            var container = stronger.Members.FirstOrDefault(strongerMember =>
                fileSystem.PathsEqual(member.Path, strongerMember.Path)
                || fileSystem.IsDescendant(member.Path, strongerMember.Path));

            if (container is null)
                return false;

            if (!fileSystem.PathsEqual(member.Path, container.Path))
                strictlyContained = true;
        }

        // Do not let a larger candidate suppress a relationship represented beneath only part of it.
        foreach (var strongerMember in stronger.Members)
        {
            if (!candidate.Members.Any(member =>
                    fileSystem.PathsEqual(member.Path, strongerMember.Path)
                    || fileSystem.IsDescendant(member.Path, strongerMember.Path)))
                return false;
        }

        return strictlyContained;
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
