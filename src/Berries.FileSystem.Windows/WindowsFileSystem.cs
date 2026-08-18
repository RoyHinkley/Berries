using Berries.FileSystem.Abstractions;

namespace Berries.FileSystem.Windows;

/// <summary>Windows/NTFS adapter. Platform-specific path and traversal policy belongs here.</summary>
public sealed class WindowsFileSystem : IFileSystem
{
    public FileSystemPath NormalizePath(FileSystemPath path) =>
        new(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Value)));

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

    public Stream OpenRead(FileSystemPath path) => File.OpenRead(path.Value);

    public bool Exists(FileSystemPath path) => throw new NotImplementedException();
    public void CreateDirectory(FileSystemPath path) => throw new NotImplementedException();
    public void CopyFile(FileSystemPath source, FileSystemPath destination) => throw new NotImplementedException();
    public void MoveFile(FileSystemPath source, FileSystemPath destination) => throw new NotImplementedException();
    public void DeleteFile(FileSystemPath path) => throw new NotImplementedException();
    public void RemoveDirectory(FileSystemPath path) => throw new NotImplementedException();
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
