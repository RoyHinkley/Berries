using Berries.FileSystem.Abstractions;

namespace Berries.Core;

/// <summary>Platform-neutral progress reported while hashing duplicate candidates.</summary>
public sealed record DuplicateDiscoveryProgress(
    long FilesHashed,
    long CandidateFiles,
    long BytesHashed,
    long CandidateBytes,
    FileSystemPath CurrentPath);
