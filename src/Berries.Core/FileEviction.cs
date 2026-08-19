using Berries.Core.Domain;

namespace Berries.Core;

/// <summary>Records a file removed from the current portrait after a filesystem access failure.</summary>
public sealed record FileEviction(
    FileInstance File,
    string Operation,
    string Reason);
