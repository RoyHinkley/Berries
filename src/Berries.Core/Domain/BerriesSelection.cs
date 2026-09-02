using Berries.Core.Analysis;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Domain;

/// <summary>
/// Compact selection-derived Directory state. None distinguishes an empty selection
/// from a selection spanning three or more direct parent Directories.
/// CommonAncestor is the deepest Directory within one Corpus Root containing every selected file.
/// </summary>
public readonly record struct SelectedDirectories(
    bool None,
    FileSystemPath? Single,
    (FileSystemPath First, FileSystemPath Second)? Pair,
    FileSystemPath? CommonAncestor);

/// <summary>
/// The persistent semantic selection for a Berries session. Selection is always a literal
/// set of files in the current Working Portrait; projections merely display or modify it.
/// </summary>
public sealed class BerriesSelection
{
    private readonly IFileSystem fileSystem;
    private readonly Corpus corpus;
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, FileInstance> filesByPath = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, FileInstance[]> filesByDirectory = new(StringComparer.OrdinalIgnoreCase);

    public BerriesSelection(IFileSystem fileSystem, Corpus corpus, Portrait portrait)
    {
        this.fileSystem = fileSystem;
        this.corpus = corpus;
        Refresh(portrait);
    }

    public int Count => selected.Count;
    public bool IsEmpty => selected.Count == 0;
    public SelectedDirectories SelectedDirectories { get; private set; }

    /// <summary>Raised only when the Directory summary changes, not for every file-selection change.</summary>
    public event EventHandler? SelectedDirectoriesChanged;

    public IReadOnlyList<FileInstance> Files => selected
        .Select(key => filesByPath.GetValueOrDefault(key))
        .Where(file => file is not null)
        .Cast<FileInstance>()
        .ToArray();

    public bool Contains(FileInstance file) => selected.Contains(PathKey(file.Path));
    public bool Contains(FileSystemPath path) => selected.Contains(PathKey(path));

    public void Clear()
    {
        if (selected.Count == 0) return;
        selected.Clear();
        UpdateSelectedDirectories();
    }

    public void Add(IEnumerable<FileInstance> files)
    {
        var changed = false;
        foreach (var file in Current(files)) changed |= selected.Add(PathKey(file.Path));
        if (changed) UpdateSelectedDirectories();
    }

    public void Remove(IEnumerable<FileInstance> files)
    {
        var changed = false;
        foreach (var file in files) changed |= selected.Remove(PathKey(file.Path));
        if (changed) UpdateSelectedDirectories();
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
        UpdateSelectedDirectories();
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
        var changed = false;
        foreach (var file in Current(universe))
        {
            var key = PathKey(file.Path);
            if (!selected.Remove(key)) selected.Add(key);
            changed = true;
        }
        if (changed) UpdateSelectedDirectories();
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
        UpdateSelectedDirectories();
    }

    internal void MovePath(FileSystemPath source, FileSystemPath destination)
    {
        var sourceKey = PathKey(source);
        if (!selected.Remove(sourceKey)) return;
        selected.Add(PathKey(destination));
        UpdateSelectedDirectories();
    }

    private void UpdateSelectedDirectories()
    {
        var next = AnalyzeSelectedDirectories();
        if (next == SelectedDirectories) return;
        SelectedDirectories = next;
        SelectedDirectoriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private SelectedDirectories AnalyzeSelectedDirectories()
    {
        if (selected.Count == 0)
            return new SelectedDirectories(true, null, null, null);

        var directories = new Dictionary<string, FileSystemPath>(StringComparer.OrdinalIgnoreCase);
        FileSystemPath? commonAncestor = null;
        FileSystemPath? commonRoot = null;
        var representedFileCount = 0;

        foreach (var path in selected)
        {
            if (!filesByPath.TryGetValue(path, out var file)) continue;
            representedFileCount++;

            var directory = file.ParentDirectory;
            if (directories.Count < 3)
                directories.TryAdd(PathKey(directory), new FileSystemPath(PathKey(directory)));

            var root = FindContainingRoot(directory);
            if (root is null)
            {
                commonAncestor = null;
                commonRoot = null;
                continue;
            }

            if (representedFileCount == 1)
            {
                commonRoot = root;
                commonAncestor = directory;
                continue;
            }

            if (commonRoot is null || !fileSystem.PathsEqual(commonRoot.Value, root.Value))
            {
                commonAncestor = null;
                commonRoot = null;
                continue;
            }

            if (commonAncestor is not null)
                commonAncestor = FindCommonAncestor(commonAncestor.Value, directory, commonRoot.Value);
        }

        if (representedFileCount == 0)
            return new SelectedDirectories(true, null, null, null);

        if (directories.Count == 1)
            return new SelectedDirectories(false, directories.Values.First(), null, commonAncestor);

        if (directories.Count == 2)
        {
            var pair = directories.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Value)
                .ToArray();
            return new SelectedDirectories(false, null, (pair[0], pair[1]), commonAncestor);
        }

        return new SelectedDirectories(false, null, null, commonAncestor);
    }

    private FileSystemPath? FindContainingRoot(FileSystemPath directory)
    {
        foreach (var root in corpus.Roots)
        {
            if (fileSystem.PathsEqual(directory, root.Path) || fileSystem.IsDescendant(directory, root.Path))
                return root.Path;
        }
        return null;
    }

    private FileSystemPath? FindCommonAncestor(
        FileSystemPath candidate,
        FileSystemPath directory,
        FileSystemPath root)
    {
        var ancestor = candidate;
        while (!fileSystem.PathsEqual(directory, ancestor) && !fileSystem.IsDescendant(directory, ancestor))
        {
            if (fileSystem.PathsEqual(ancestor, root))
                return null;

            var parent = fileSystem.GetParentDirectory(ancestor);
            if (parent is null
                || (!fileSystem.PathsEqual(parent.Value, root) && !fileSystem.IsDescendant(parent.Value, root)))
                return null;

            ancestor = parent.Value;
        }
        return ancestor;
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
