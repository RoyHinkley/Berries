using Berries.FileSystem.Abstractions;

namespace Berries.Core.Decisions;

public sealed record DirectoryMapping(FileSystemPath Source, FileSystemPath Destination);
