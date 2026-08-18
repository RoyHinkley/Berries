namespace Berries.FileSystem.Abstractions;

/// <summary>
/// Least-common-denominator filesystem contract required by the initial application.
/// Symbolic/reparse/special-file filtering is the adapter's responsibility.
/// </summary>
public interface IFileSystem
{
    FileSystemPath NormalizePath(FileSystemPath path);
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
