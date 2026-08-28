using Berries.FileSystem.Abstractions;

namespace Berries.FileSystem.Windows;

/// <summary>Windows/NTFS adapter. Platform-specific path and traversal policy belongs here.</summary>
public sealed class WindowsFileSystem : IFileSystem
{
    public FileSystemPath NormalizePath(FileSystemPath path) =>
        new(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Value)));

    public FileSystemPath? GetParentDirectory(FileSystemPath path)
    {
        var parent = Directory.GetParent(NormalizePath(path).Value);
        return parent is null
            ? null
            : new FileSystemPath(Path.TrimEndingDirectorySeparator(parent.FullName));
    }

    public FileSystemPath GetRelativePath(FileSystemPath relativeTo, FileSystemPath path) =>
        new(Path.GetRelativePath(NormalizePath(relativeTo).Value, NormalizePath(path).Value));

    public FileSystemPath Combine(FileSystemPath directory, FileSystemPath relativePath) =>
        NormalizePath(new FileSystemPath(Path.Combine(directory.Value, relativePath.Value)));

    public IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        foreach (var path in Directory.EnumerateFiles(root.Value, "*", options))
        {
            var info = new FileInfo(path);
            var parent = info.DirectoryName
                ?? throw new IOException($"File has no parent directory: {path}");

            yield return new FileSystemFile(
                new FileSystemPath(info.FullName),
                new FileSystemPath(parent),
                info.Length,
                info.LastWriteTimeUtc);
        }
    }

    public Stream OpenRead(FileSystemPath path) => new FileStream(
        path.Value,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);

    public bool Exists(FileSystemPath path) => File.Exists(path.Value) || Directory.Exists(path.Value);

    public void CreateDirectory(FileSystemPath path) => Directory.CreateDirectory(path.Value);

    public void CopyFile(FileSystemPath source, FileSystemPath destination) =>
        File.Copy(source.Value, destination.Value, overwrite: false);

    public void MoveFile(FileSystemPath source, FileSystemPath destination) =>
        File.Move(source.Value, destination.Value, overwrite: false);

    public void DeleteFile(FileSystemPath path) => File.Delete(path.Value);

    public void RemoveDirectory(FileSystemPath path) => Directory.Delete(path.Value, recursive: false);

    public bool PathsEqual(FileSystemPath left, FileSystemPath right) =>
        StringComparer.OrdinalIgnoreCase.Equals(
            NormalizePath(left).Value,
            NormalizePath(right).Value);

    public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor)
    {
        var candidatePath = NormalizePath(candidate).Value;
        var ancestorPath = NormalizePath(ancestor).Value;

        if (StringComparer.OrdinalIgnoreCase.Equals(candidatePath, ancestorPath))
            return false;

        var relative = Path.GetRelativePath(ancestorPath, candidatePath);
        return relative != "."
            && !Path.IsPathRooted(relative)
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
