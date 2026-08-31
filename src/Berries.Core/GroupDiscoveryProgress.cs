using Berries.FileSystem.Abstractions;

namespace Berries.Core;

/// <summary>Platform-neutral progress reported while discovering Groups.</summary>
public sealed record GroupDiscoveryProgress(
    long FilesHashed,
    long CandidateFiles,
    long BytesHashed,
    long CandidateBytes,
    FileSystemPath CurrentPath,
    string Phase = "Hashing Group candidates");
