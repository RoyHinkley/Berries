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

        var minHashByNamesake = (minHashAnalysis?.Candidates ?? [])
            .GroupBy(candidate => candidate.Namesake, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var workingFiles = session.WorkingPortrait.Files;
        var groups = session.Groups;
        var measurements = new List<Measurement>(namesakes.Length);

        foreach (var population in namesakes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool UnderOccurrence(FileSystemPath path) => population.Occurrences.Any(occurrence =>
                fileSystem.PathsEqual(path, occurrence)
                || fileSystem.IsDescendant(path, occurrence));

            var files = workingFiles
                .Where(file => UnderOccurrence(file.ParentDirectory))
                .ToArray();
            var filePaths = files.Select(file => file.Path).ToHashSet();

            var occurrencesWithGroupedFiles = population.Occurrences.Count(occurrence =>
                files.Any(file =>
                    fileSystem.PathsEqual(file.ParentDirectory, occurrence)
                    || fileSystem.IsDescendant(file.ParentDirectory, occurrence)));

            var uniqueFileCount = session.UniqueFileCountsByDirectory
                .Where(item => UnderOccurrence(item.Key))
                .Sum(item => item.Value);

            var touchedGroups = 0;
            var resolvedGroups = 0;
            var excessCopiesRemoved = 0;

            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var before = group.Files.Count;
                if (before == 0)
                    continue;

                var removed = group.Files.Count(file => filePaths.Contains(file.Path));
                if (removed == 0)
                    continue;

                touchedGroups++;
                var after = before - removed;
                if (before > 1 && after <= 1)
                    resolvedGroups++;

                excessCopiesRemoved += Math.Max(0, before - 1) - Math.Max(0, after - 1);
            }

            minHashByNamesake.TryGetValue(population.Name, out var structural);
            var structuralFamilyCount = structural?.IntrinsicFamilyCount ?? 0;
            var structuralSupportingOccurrenceCount = structural?.IntrinsicSupportingOccurrenceCount ?? 0;
            var structuralSupportFraction = population.Occurrences.Count == 0
                ? 0
                : structuralSupportingOccurrenceCount / (double)population.Occurrences.Count;

            measurements.Add(new Measurement(
                population.Name,
                population.Occurrences.Count,
                occurrencesWithGroupedFiles,
                files.Length,
                uniqueFileCount,
                touchedGroups,
                resolvedGroups,
                excessCopiesRemoved,
                structuralFamilyCount,
                structuralSupportingOccurrenceCount,
                structuralSupportFraction));
        }

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
