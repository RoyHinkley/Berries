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
    public static IReadOnlyList<DirectoryNamesakeProjection> Build(BerriesSession session)
    {
        var currentFilesByDirectory = session.WorkingPortrait.Files
            .GroupBy(file => file.ParentDirectory)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FileInstance>)group.ToArray());

        var directories = new HashSet<FileSystemPath>(session.UniqueFileCountsByDirectory.Keys);
        foreach (var directory in currentFilesByDirectory.Keys)
            directories.Add(directory);

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
}
