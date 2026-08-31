using Berries.Core.Analysis;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Domain;

/// <summary>
/// The persistent semantic selection for a Berries session. Selection is always a literal
/// set of files in the current Working Portrait; projections merely display or modify it.
/// </summary>
public sealed class BerriesSelection
{
    private readonly IFileSystem fileSystem;
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, FileInstance> filesByPath = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, FileInstance[]> filesByDirectory = new(StringComparer.OrdinalIgnoreCase);

    public BerriesSelection(IFileSystem fileSystem, Portrait portrait)
    {
        this.fileSystem = fileSystem;
        Refresh(portrait);
    }

    public int Count => selected.Count;
    public bool IsEmpty => selected.Count == 0;

    public IReadOnlyList<FileInstance> Files => selected
        .Select(key => filesByPath.GetValueOrDefault(key))
        .Where(file => file is not null)
        .Cast<FileInstance>()
        .ToArray();

    public bool Contains(FileInstance file) => selected.Contains(PathKey(file.Path));
    public bool Contains(FileSystemPath path) => selected.Contains(PathKey(path));

    public void Clear() => selected.Clear();

    public void Add(IEnumerable<FileInstance> files)
    {
        foreach (var file in Current(files)) selected.Add(PathKey(file.Path));
    }

    public void Remove(IEnumerable<FileInstance> files)
    {
        foreach (var file in files) selected.Remove(PathKey(file.Path));
    }

    public void Toggle(IEnumerable<FileInstance> files)
    {
        var current = Current(files).ToArray();
        if (current.Length == 0) return;

        var remove = current.All(file => selected.Contains(PathKey(file.Path)));
        foreach (var file in current)
        {
            var key = PathKey(file.Path);
            if (remove) selected.Remove(key); else selected.Add(key);
        }
    }

    public void ToggleDirectory(FileSystemPath directory)
    {
        var key = PathKey(directory);
        if (filesByDirectory.TryGetValue(key, out var files)) Toggle(files);
    }

    public void ToggleBranch(FileSystemPath branch)
    {
        var files = filesByDirectory
            .Where(pair => fileSystem.PathsEqual(new FileSystemPath(pair.Key), branch)
                || fileSystem.IsDescendant(new FileSystemPath(pair.Key), branch))
            .SelectMany(pair => pair.Value);
        Toggle(files);
    }

    /// <summary>Invert among complete Groups containing at least one selected file: G(S) - S.</summary>
    public void InvertSelectedCopies(IReadOnlyList<Group> groups)
    {
        if (selected.Count == 0) return;
        var universe = groups
            .Where(group => group.Files.Any(Contains))
            .SelectMany(group => group.Files);
        Invert(universe);
    }

    /// <summary>Invert within an explicitly supplied universe, such as all Groups in a Groups projection.</summary>
    public void Invert(IEnumerable<FileInstance> universe)
    {
        foreach (var file in Current(universe))
        {
            var key = PathKey(file.Path);
            if (!selected.Remove(key)) selected.Add(key);
        }
    }

    public int CountGroups(IReadOnlyList<Group> groups) =>
        groups.Count(group => group.Files.Any(Contains));

    public int CountOutside(IEnumerable<FileInstance> representedFiles)
    {
        var represented = Current(representedFiles).Select(file => PathKey(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return selected.Count(key => !represented.Contains(key));
    }

    internal void Refresh(Portrait portrait)
    {
        filesByPath = portrait.Files.ToDictionary(file => PathKey(file.Path), StringComparer.OrdinalIgnoreCase);
        filesByDirectory = portrait.Files
            .GroupBy(file => PathKey(file.ParentDirectory), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        selected.RemoveWhere(key => !filesByPath.ContainsKey(key));
    }

    internal void MovePath(FileSystemPath source, FileSystemPath destination)
    {
        var sourceKey = PathKey(source);
        if (!selected.Remove(sourceKey)) return;
        selected.Add(PathKey(destination));
    }

    private IEnumerable<FileInstance> Current(IEnumerable<FileInstance> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requested in files)
        {
            var key = PathKey(requested.Path);
            if (seen.Add(key) && filesByPath.TryGetValue(key, out var current)) yield return current;
        }
    }

    private string PathKey(FileSystemPath path) => fileSystem.NormalizePath(path).Value;
}
