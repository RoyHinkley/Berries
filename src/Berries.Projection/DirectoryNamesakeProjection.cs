using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Projection;

public sealed record DirectoryNamesakeProjection(
    string Name,
    IReadOnlyList<DirectoryNamesakeDirectory> Directories,
    IReadOnlyList<FileInstance> Files);

public sealed record DirectoryNamesakeDirectory(
    FileSystemPath Path,
    IReadOnlyList<FileInstance> Files);

public static class DirectoryNamesakeProjections
{
    public static IReadOnlyList<DirectoryNamesakeProjection> Build(
        BerriesSession session,
        IFileSystem fileSystem)
    {
        var currentFilesByDirectory = session.WorkingPortrait.Files
            .GroupBy(file => file.ParentDirectory)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FileInstance>)group.ToArray());

        var directories = new HashSet<FileSystemPath>();
        foreach (var root in session.Corpus.Roots)
            directories.Add(root.Path);

        foreach (var directory in session.UniqueFileCountsByDirectory.Keys)
            AddAncestorsWithinCorpus(directory, session.Corpus, fileSystem, directories);

        foreach (var directory in currentFilesByDirectory.Keys)
            AddAncestorsWithinCorpus(directory, session.Corpus, fileSystem, directories);

        return directories
            .GroupBy(directory => Path.GetFileName(directory.Value), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group =>
            {
                var members = group
                    .OrderBy(directory => directory.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(directory => new DirectoryNamesakeDirectory(
                        directory,
                        currentFilesByDirectory.TryGetValue(directory, out var files) ? files : []))
                    .ToArray();

                return new DirectoryNamesakeProjection(
                    group.Key,
                    members,
                    members.SelectMany(member => member.Files).ToArray());
            })
            .OrderByDescending(namesake => namesake.Directories.Count)
            .ThenBy(namesake => namesake.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
            if (parent is null
                || !corpus.Roots.Any(root =>
                    fileSystem.PathsEqual(parent.Value, root.Path)
                    || fileSystem.IsDescendant(parent.Value, root.Path)))
                return;

            current = parent.Value;
        }
    }
}
