using Berries.Core.Domain;

namespace Berries.Core.Cases;

public abstract record Case(IReadOnlyList<FileInstance> Files, int Leverage)
{
    public bool Hidden { get; init; }
}
