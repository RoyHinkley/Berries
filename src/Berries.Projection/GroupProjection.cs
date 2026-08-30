using Berries.Core.Domain;

namespace Berries.Projection;

public sealed record GroupProjection(
    string Label,
    IReadOnlyList<FileInstance> Files,
    IReadOnlyList<GroupProjectionFile> Items);

public sealed record GroupProjectionFile(string Label, FileInstance File);
