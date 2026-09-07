using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

public sealed record DirectoryNamesakeLeverageAnalysis(
    IReadOnlyList<DirectoryNamesakeLeverageCandidate> Candidates);

public sealed record DirectoryNamesakeLeverageCandidate(
    string Namesake,
    int TotalOccurrences,
    int OccurrencesWithGroupedFiles,
    int GroupedFileCount,
    int UniqueFileCount,
    int TouchedGroupCount,
    int ResolvedGroupCount,
    int ExcessCopiesRemoved,
    int StructuralFamilyCount,
    int StructuralSupportingOccurrenceCount,
    double StructuralSupportFraction,
    int RankByResolvedGroups,
    int RankByExcessCopiesRemoved,
    int RankByGroupedFiles,
    int RankByOccurrences,
    int RankByStructuralSupport);

/// <summary>
/// Experimental Directory Namesake disposition-leverage measurements.
///
/// Every repeated directory name is eligible. No semantic knowledge of directory names is used,
/// and structural MinHash evidence is optional rather than an admission requirement.
///
/// The analyzer deliberately exposes several independent rankings instead of blending them into
/// one score. The experiment asks whether objective Corpus measurements naturally surface useful
/// name-wide exclusion questions.
/// </summary>
public static class DirectoryNamesakeLeverageAnalyzer
{
    public static DirectoryNamesakeLeverageAnalysis Analyze(
        BerriesSession session,
        IFileSystem fileSystem,
        DirectoryNamesakeMinHashAnalysis? minHashAnalysis = null,
        CancellationToken cancellationToken = default)
    {
        var directories = BuildDirectoryInventory(session, fileSystem, cancellationToken);
        var namesakes = directories
            .Select(path => (Path: path, Name: Path.GetFileName(path.Value)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new NamesakePopulation(
                group.Key,
                group.Select(item => item.Path).ToArray()))
            .ToArray();

        var minHashByNamesake = (minHashAnalysis?.IntrinsicCandidates ?? [])
            .GroupBy(candidate => candidate.Namesake, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var occurrenceByDirectory = namesakes
            .SelectMany(population => population.Occurrences.Select(path => (Path: path, population.Name)))
            .ToDictionary(item => item.Path, item => item.Name);

        var accumulators = namesakes.ToDictionary(
            population => population.Name,
            population => new Accumulator(population.Occurrences.Count),
            StringComparer.OrdinalIgnoreCase);

        // A file can lie beneath several different Namesake occurrences. Walk its ancestor chain
        // once and accumulate all affected Namesakes rather than rescanning every file for every name.
        var namesakesByFile = new Dictionary<FileSystemPath, HashSet<string>>();
        foreach (var file in session.WorkingPortrait.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var affectedNamesakes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = file.ParentDirectory;

            while (InsideCorpus(current, session.Corpus, fileSystem))
            {
                if (occurrenceByDirectory.TryGetValue(current, out var namesake))
                {
                    affectedNamesakes.Add(namesake);
                    accumulators[namesake].OccurrencesWithGroupedFiles.Add(current);
                }

                if (session.Corpus.Roots.Any(root => fileSystem.PathsEqual(current, root.Path)))
                    break;

                var parent = fileSystem.GetParentDirectory(current);
                if (parent is null)
                    break;
                current = parent.Value;
            }

            namesakesByFile[file.Path] = affectedNamesakes;
            foreach (var namesake in affectedNamesakes)
                accumulators[namesake].GroupedFileCount++;
        }

        // Unique-file counts are fixed session metadata. Attribute each directory's count to every
        // Namesake ancestor once; nested occurrences of the same name must not double count it.
        foreach (var (directory, count) in session.UniqueFileCountsByDirectory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var affectedNamesakes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = directory;

            while (InsideCorpus(current, session.Corpus, fileSystem))
            {
                if (occurrenceByDirectory.TryGetValue(current, out var namesake))
                    affectedNamesakes.Add(namesake);

                if (session.Corpus.Roots.Any(root => fileSystem.PathsEqual(current, root.Path)))
                    break;

                var parent = fileSystem.GetParentDirectory(current);
                if (parent is null)
                    break;
                current = parent.Value;
            }

            foreach (var namesake in affectedNamesakes)
                accumulators[namesake].UniqueFileCount += count;
        }

        // For each Group, count how many members a name-wide exclusion would remove. This computes
        // disposition leverage in one pass over Group members rather than one pass over Groups per name.
        foreach (var group in session.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = group.Files.Count;
            if (before == 0)
                continue;

            var removedByNamesake = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in group.Files)
            {
                if (!namesakesByFile.TryGetValue(file.Path, out var affectedNamesakes))
                    continue;

                foreach (var namesake in affectedNamesakes)
                    removedByNamesake[namesake] = removedByNamesake.GetValueOrDefault(namesake) + 1;
            }

            foreach (var (namesake, removed) in removedByNamesake)
            {
                var accumulator = accumulators[namesake];
                accumulator.TouchedGroupCount++;
                var after = before - removed;
                if (before > 1 && after <= 1)
                    accumulator.ResolvedGroupCount++;

                accumulator.ExcessCopiesRemoved +=
                    Math.Max(0, before - 1) - Math.Max(0, after - 1);
            }
        }

        var measurements = namesakes.Select(population =>
        {
            var accumulator = accumulators[population.Name];
            minHashByNamesake.TryGetValue(population.Name, out var structural);
            var structuralFamilyCount = structural?.IntrinsicFamilyCount ?? 0;
            var structuralSupportingOccurrenceCount = structural?.IntrinsicSupportingOccurrenceCount ?? 0;
            var structuralSupportFraction = population.Occurrences.Count == 0
                ? 0
                : structuralSupportingOccurrenceCount / (double)population.Occurrences.Count;

            return new Measurement(
                population.Name,
                population.Occurrences.Count,
                accumulator.OccurrencesWithGroupedFiles.Count,
                accumulator.GroupedFileCount,
                accumulator.UniqueFileCount,
                accumulator.TouchedGroupCount,
                accumulator.ResolvedGroupCount,
                accumulator.ExcessCopiesRemoved,
                structuralFamilyCount,
                structuralSupportingOccurrenceCount,
                structuralSupportFraction);
        }).ToArray();

        var resolvedRanks = Rank(measurements,
            item => item.ResolvedGroupCount,
            item => item.ExcessCopiesRemoved,
            item => item.GroupedFileCount);
        var excessRanks = Rank(measurements,
            item => item.ExcessCopiesRemoved,
            item => item.ResolvedGroupCount,
            item => item.GroupedFileCount);
        var groupedRanks = Rank(measurements,
            item => item.GroupedFileCount,
            item => item.ResolvedGroupCount,
            item => item.TotalOccurrences);
        var occurrenceRanks = Rank(measurements,
            item => item.TotalOccurrences,
            item => item.GroupedFileCount,
            item => item.ResolvedGroupCount);
        var structuralRanks = measurements
            .OrderByDescending(item => item.StructuralSupportingOccurrenceCount)
            .ThenByDescending(item => item.StructuralSupportFraction)
            .ThenByDescending(item => item.StructuralFamilyCount)
            .ThenBy(item => item.Namesake, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => (item.Namesake, Rank: index + 1))
            .ToDictionary(item => item.Namesake, item => item.Rank, StringComparer.OrdinalIgnoreCase);

        var candidates = measurements
            .Select(item => new DirectoryNamesakeLeverageCandidate(
                item.Namesake,
                item.TotalOccurrences,
                item.OccurrencesWithGroupedFiles,
                item.GroupedFileCount,
                item.UniqueFileCount,
                item.TouchedGroupCount,
                item.ResolvedGroupCount,
                item.ExcessCopiesRemoved,
                item.StructuralFamilyCount,
                item.StructuralSupportingOccurrenceCount,
                item.StructuralSupportFraction,
                resolvedRanks[item.Namesake],
                excessRanks[item.Namesake],
                groupedRanks[item.Namesake],
                occurrenceRanks[item.Namesake],
                structuralRanks[item.Namesake]))
            .OrderBy(candidate => candidate.RankByResolvedGroups)
            .ToArray();

        return new DirectoryNamesakeLeverageAnalysis(candidates);
    }

    private static Dictionary<string, int> Rank(
        IEnumerable<Measurement> measurements,
        Func<Measurement, int> primary,
        Func<Measurement, int> secondary,
        Func<Measurement, int> tertiary) =>
        measurements
            .OrderByDescending(primary)
            .ThenByDescending(secondary)
            .ThenByDescending(tertiary)
            .ThenBy(item => item.Namesake, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => (item.Namesake, Rank: index + 1))
            .ToDictionary(item => item.Namesake, item => item.Rank, StringComparer.OrdinalIgnoreCase);

    private sealed record NamesakePopulation(
        string Name,
        IReadOnlyList<FileSystemPath> Occurrences);

    private sealed class Accumulator(int totalOccurrences)
    {
        public int TotalOccurrences { get; } = totalOccurrences;
        public HashSet<FileSystemPath> OccurrencesWithGroupedFiles { get; } = [];
        public int GroupedFileCount { get; set; }
        public int UniqueFileCount { get; set; }
        public int TouchedGroupCount { get; set; }
        public int ResolvedGroupCount { get; set; }
        public int ExcessCopiesRemoved { get; set; }
    }

    private sealed record Measurement(
        string Namesake,
        int TotalOccurrences,
        int OccurrencesWithGroupedFiles,
        int GroupedFileCount,
        int UniqueFileCount,
        int TouchedGroupCount,
        int ResolvedGroupCount,
        int ExcessCopiesRemoved,
        int StructuralFamilyCount,
        int StructuralSupportingOccurrenceCount,
        double StructuralSupportFraction);

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
