namespace Berries.Core.Decisions;

public enum DispositionIssueKind
{
    Incomplete,
    DestinationCollision,
    MissingSource,
    InvalidMapping
}

public sealed record DispositionIssue(DispositionIssueKind Kind, string Message);

public sealed record DispositionValidationResult(IReadOnlyList<DispositionIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public interface IDispositionValidator
{
    DispositionValidationResult Validate(Disposition disposition);
}
