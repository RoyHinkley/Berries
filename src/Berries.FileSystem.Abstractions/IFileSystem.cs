namespace Berries.FileSystem.Abstractions;

/// <summary>
/// Least-common-denominator filesystem contract required by the application.
/// Symbolic/reparse/special-file filtering is the adapter's responsibility.
/// </summary>
public interface IFileSystem
{
    FileSystemPath NormalizePath(FileSystemPath path);
    FileSystemPath? GetParentDirectory(FileSystemPath path);

    FileSystemPath GetRelativePath(FileSystemPath relativeTo, FileSystemPath path) =>
        throw new NotSupportedException("This filesystem adapter does not provide relative-path construction.");

    FileSystemPath Combine(FileSystemPath directory, FileSystemPath relativePath) =>
        throw new NotSupportedException("This filesystem adapter does not provide path construction.");

    IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath root);
    Stream OpenRead(FileSystemPath path);

    bool Exists(FileSystemPath path);
    void CreateDirectory(FileSystemPath path);
    void CopyFile(FileSystemPath source, FileSystemPath destination);
    void MoveFile(FileSystemPath source, FileSystemPath destination);
    void DeleteFile(FileSystemPath path);
    void RemoveDirectory(FileSystemPath path);

    bool PathsEqual(FileSystemPath left, FileSystemPath right);
    bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor);
}
