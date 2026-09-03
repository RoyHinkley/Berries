using System.Text;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record DirectoryNamesakeMinHashAnalysis(
    IReadOnlyList<DirectoryNamesakeMinHashNamesakeCandidate> Candidates,
    IReadOnlyList<DirectoryNamesakeMinHashNamesakeCandidate> IntrinsicCandidates);

public sealed record DirectoryNamesakeMinHashNamesakeCandidate(
    string Namesake,
    int TotalOccurrences,
    int IntrinsicFamilyCount,
    int IntrinsicSupportingOccurrenceCount,
    int ResidualFamilyCount,
    int ResidualSupportingOccurrenceCount,
    IReadOnlyList<DirectoryNamesakeMinHashFamily> Families);

public sealed record DirectoryNamesakeMinHashFamily(
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
/// MinHash/LSH discovers similar structural families. Families are evidence for one Namesake-level
/// exclusion candidate. Namesakes are then ranked greedily by residual evidence: once a Namesake is
/// chosen, all descendants beneath every occurrence of that name are covered and cease contributing
/// evidence to later candidates. IntrinsicCandidates preserves the pre-greedy evidence for experiments
/// that must not inherit the greedy coverage bias.
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

        var occurrencesByNamesake = namesakes.ToDictionary(
            group => group.Key.ToUpperInvariant(),
            group => (IReadOnlyList<FileSystemPath>)group.Select(item => item.Path).ToArray(),
            StringComparer.OrdinalIgnoreCase);

        var featuresByDirectory = new Dictionary<FileSystemPath, HashSet<string>>();
        var descendantOccurrenceCounts = new Dictionary<FileSystemPath, int>();
        var maxDescendantDepths = new Dictionary<FileSystemPath, int>();

        foreach (var directory in namesakeByDirectory.Keys)
        {
            featuresByDirectory[directory] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            descendantOccurrenceCounts[directory] = 0;
            maxDescendantDepths[directory] = 0;
        }

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

        var collections = new Dictionary<string, FamilyAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in bandBuckets.Where(item => item.Value.Count > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var members = bucket.Value
                .OrderBy(member => member.Path.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var key = bucket.Key.Namesake + "\n" + string.Join("\n", members.Select(member => member.Path.Value));

            if (!collections.TryGetValue(key, out var accumulator))
                collections[key] = accumulator = new FamilyAccumulator(bucket.Key.Namesake, members);
            accumulator.Bands.Add(bucket.Key.Band);
        }

        var familiesByNamesake = collections.Values
            .Select(accumulator => new
            {
                accumulator.Namesake,
                Family = new DirectoryNamesakeMinHashFamily(
                    accumulator.Bands.Count,
                    bands,
                    accumulator.Bands.OrderBy(band => band).ToArray(),
                    accumulator.Members)
            })
            .GroupBy(item => item.Namesake, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DirectoryNamesakeMinHashFamily>)group.Select(item => item.Family).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var intrinsicSupportByNamesake = familiesByNamesake.ToDictionary(
            item => item.Key,
            item => DistinctMembers(item.Value).Count,
            StringComparer.OrdinalIgnoreCase);

        var intrinsicCandidates = familiesByNamesake
            .Select(item => new DirectoryNamesakeMinHashNamesakeCandidate(
                item.Key,
                occurrencesByNamesake[item.Key].Count,
                item.Value.Count,
                intrinsicSupportByNamesake[item.Key],
                item.Value.Count,
                intrinsicSupportByNamesake[item.Key],
                item.Value))
            .OrderBy(candidate => candidate.Namesake, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var remaining = new HashSet<string>(familiesByNamesake.Keys, StringComparer.OrdinalIgnoreCase);
        var coverageRoots = new List<FileSystemPath>();
        var rankedNamesakes = new List<DirectoryNamesakeMinHashNamesakeCandidate>();

        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var residuals = remaining
                .Select(namesake => BuildResidualCandidate(
                    namesake,
                    occurrencesByNamesake[namesake],
                    familiesByNamesake[namesake],
                    intrinsicSupportByNamesake[namesake],
                    coverageRoots,
                    fileSystem))
                .Where(candidate => candidate.ResidualFamilyCount > 0)
                .ToArray();

            if (residuals.Length == 0)
                break;

            var best = residuals
                .OrderByDescending(candidate => candidate.Families.Max(family => family.MatchingBands))
                .ThenByDescending(candidate => candidate.Families.Max(family => family.Members.Min(member => member.DescendantNamesakeCount)))
                .ThenByDescending(candidate => candidate.Families.Max(family => family.Members.Average(member => member.DescendantNamesakeCount)))
                .ThenBy(candidate => candidate.Families.Min(family => family.Members.Max(member => member.MaxDescendantNamesakeDepth)))
                .ThenBy(candidate => candidate.Families.Min(family => family.Members.Average(member => member.MaxDescendantNamesakeDepth)))
                .ThenByDescending(candidate => candidate.ResidualSupportingOccurrenceCount)
                .ThenByDescending(candidate => candidate.ResidualFamilyCount)
                .ThenBy(candidate => candidate.Namesake, StringComparer.OrdinalIgnoreCase)
                .First();

            rankedNamesakes.Add(best);
            remaining.Remove(best.Namesake);
            coverageRoots.AddRange(occurrencesByNamesake[best.Namesake]);
        }

        return new DirectoryNamesakeMinHashAnalysis(rankedNamesakes, intrinsicCandidates);
    }

    private static DirectoryNamesakeMinHashNamesakeCandidate BuildResidualCandidate(
        string namesake,
        IReadOnlyList<FileSystemPath> allOccurrences,
        IReadOnlyList<DirectoryNamesakeMinHashFamily> intrinsicFamilies,
        int intrinsicSupportingOccurrenceCount,
        IReadOnlyList<FileSystemPath> coverageRoots,
        IFileSystem fileSystem)
    {
        var residualFamilies = intrinsicFamilies
            .Select(family => new DirectoryNamesakeMinHashFamily(
                family.MatchingBands,
                family.TotalBands,
                family.Bands,
                family.Members.Where(member => !IsCovered(member.Path, coverageRoots, fileSystem)).ToArray()))
            .Where(family => family.Members.Count >= 2)
            .ToArray();

        return new DirectoryNamesakeMinHashNamesakeCandidate(
            namesake,
            allOccurrences.Count,
            intrinsicFamilies.Count,
            intrinsicSupportingOccurrenceCount,
            residualFamilies.Length,
            DistinctMembers(residualFamilies).Count,
            residualFamilies);
    }

    private static HashSet<FileSystemPath> DistinctMembers(IEnumerable<DirectoryNamesakeMinHashFamily> families) =>
        families.SelectMany(family => family.Members).Select(member => member.Path).ToHashSet();

    private static bool IsCovered(
        FileSystemPath path,
        IReadOnlyList<FileSystemPath> coverageRoots,
        IFileSystem fileSystem) =>
        coverageRoots.Any(root =>
            fileSystem.PathsEqual(path, root)
            || fileSystem.IsDescendant(path, root));

    private sealed class FamilyAccumulator(
        string namesake,
        IReadOnlyList<DirectoryNamesakeMinHashMember> members)
    {
        public string Namesake { get; } = namesake;
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
