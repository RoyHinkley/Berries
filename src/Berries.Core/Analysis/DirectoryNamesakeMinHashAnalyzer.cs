using System.Text;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record DirectoryNamesakeMinHashBucket(
    int Band,
    ulong BandHash,
    IReadOnlyList<DirectoryNamesakeMinHashMember> Members);

public sealed record DirectoryNamesakeMinHashMember(
    FileSystemPath Path,
    int DescendantNamesakeCount);

/// <summary>
/// Experimental fuzzy structural comparison of Directory Namesakes.
/// Each Namesake Directory is represented by the set of Namesake leaf names strictly beneath it.
/// A MinHash signature approximates Jaccard similarity of those sets; LSH bands expose raw
/// same-band buckets without an exhaustive pairwise comparison.
/// </summary>
public static class DirectoryNamesakeMinHashAnalyzer
{
    public static IReadOnlyList<DirectoryNamesakeMinHashBucket> Analyze(
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

        var namesakeNames = new HashSet<string>(
            namesakes.Select(group => group.Key),
            StringComparer.OrdinalIgnoreCase);
        var namesakeDirectories = namesakes
            .SelectMany(group => group.Select(item => item.Path))
            .Distinct()
            .ToArray();

        var descendantNamesakes = new Dictionary<FileSystemPath, HashSet<string>>();
        foreach (var directory in namesakeDirectories)
            descendantNamesakes[directory] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Walk each Namesake occurrence upward and add its leaf name to every Namesake ancestor.
        // Starting at the parent makes the feature relationship strictly descendant, not self.
        foreach (var group in namesakes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var feature = group.Key;
            foreach (var occurrence in group.Select(item => item.Path))
            {
                var current = fileSystem.GetParentDirectory(occurrence);
                while (current is { } ancestor && InsideCorpus(ancestor, session.Corpus, fileSystem))
                {
                    if (descendantNamesakes.TryGetValue(ancestor, out var features))
                        features.Add(feature);

                    if (session.Corpus.Roots.Any(root => fileSystem.PathsEqual(ancestor, root.Path)))
                        break;
                    current = fileSystem.GetParentDirectory(ancestor);
                }
            }
        }

        var bands = signatureLength / rowsPerBand;
        var buckets = new Dictionary<(int Band, ulong Hash), List<DirectoryNamesakeMinHashMember>>();

        foreach (var item in descendantNamesakes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Value.Count < minimumDescendantNamesakes)
                continue;

            var signature = MinHashSignature(item.Value, signatureLength);
            for (var band = 0; band < bands; band++)
            {
                var bandHash = HashBand(signature, band * rowsPerBand, rowsPerBand);
                var key = (band, bandHash);
                if (!buckets.TryGetValue(key, out var members))
                    buckets[key] = members = [];
                members.Add(new DirectoryNamesakeMinHashMember(item.Key, item.Value.Count));
            }
        }

        return buckets
            .Where(item => item.Value.Count > 1)
            .Select(item => new DirectoryNamesakeMinHashBucket(
                item.Key.Band,
                item.Key.Hash,
                item.Value
                    .OrderBy(member => member.Path.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderByDescending(bucket => bucket.Members.Count)
            .ThenBy(bucket => bucket.Band)
            .ThenBy(bucket => bucket.BandHash)
            .Take(resultLimit)
            .ToArray();
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
