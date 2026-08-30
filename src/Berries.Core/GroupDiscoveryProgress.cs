using Berries.FileSystem.Abstractions;

namespace Berries.Core;

/// <summary>Platform-neutral progress reported while hashing Group candidates.</summary>
public sealed record GroupDiscoveryProgress(
    long FilesHashed,
    long CandidateFiles,
    long BytesHashed,
    long CandidateBytes,
    FileSystemPath CurrentPath);
