using Berries.Core.Analysis;

namespace Berries.Gui;

public sealed record SprinkledDuplicateCandidate(
    DuplicateSet DuplicateSet,
    string FileName,
    int InstanceCount,
    int DirectoryCount);
