using Berries.FileSystem.Abstractions;

namespace Berries.Core.Domain;

public sealed record CorpusRoot(FileSystemPath Path);

public sealed class Corpus
{
    public Corpus(IEnumerable<CorpusRoot> roots)
    {
        Roots = roots.ToArray();
        if (Roots.Count == 0)
            throw new ArgumentException("A corpus must contain at least one root.", nameof(roots));
    }

    public IReadOnlyList<CorpusRoot> Roots { get; }
}
