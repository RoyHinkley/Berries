using Berries.FileSystem.Abstractions;

namespace Berries.Core.Planning;

public abstract record FileAction;
public sealed record DeleteFileAction(FileSystemPath Path) : FileAction;
public sealed record CopyFileAction(FileSystemPath Source, FileSystemPath Destination) : FileAction;
public sealed record MoveFileAction(FileSystemPath Source, FileSystemPath Destination) : FileAction;
