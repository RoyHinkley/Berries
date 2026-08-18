namespace Berries.Core.Domain;

/// <summary>Stable identity assigned by duplicate detection to known byte content.</summary>
public readonly record struct ContentId(string Value)
{
    public override string ToString() => Value;
}
