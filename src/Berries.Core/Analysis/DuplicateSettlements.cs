using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core.Analysis;

/// <summary>
/// Records duplicate relationships that the user has semantically settled as acceptable.
/// Physical DuplicateSets remain unchanged; settlements remove evidence from subsequent
/// unresolved-duplication analysis.
/// </summary>
public sealed class DuplicateSettlements
{
    private readonly HashSet<ContentId> acceptedContents = [];
    private readonly HashSet<AcceptedPair> acceptedPairs = [];

    public int AcceptedContentCount => acceptedContents.Count;
    public int AcceptedPairCount => acceptedPairs.Count;

    /// <summary>Accept every duplicate relationship represented by this Content.</summary>
    public void Accept(DuplicateSet duplicateSet) => AcceptContent(duplicateSet.Content);

    /// <summary>Accept every duplicate relationship represented by this Content.</summary>
    public void AcceptContent(ContentId content) => acceptedContents.Add(content);

    /// <summary>Accept one specific pairwise relationship for a Content.</summary>
    public void AcceptPair(ContentId content, FileInstance first, FileInstance second) =>
        AcceptPair(content, first.Path, second.Path);

    /// <summary>Accept one specific pairwise relationship for a Content.</summary>
    public void AcceptPair(ContentId content, FileSystemPath first, FileSystemPath second)
    {
        if (first == second)
            throw new ArgumentException("An accepted duplicate pair must identify two distinct files.");

        acceptedPairs.Add(CanonicalPair(content, first, second));
    }

    public bool IsContentAccepted(ContentId content) => acceptedContents.Contains(content);

    public bool IsPairAccepted(ContentId content, FileInstance first, FileInstance second) =>
        IsPairAccepted(content, first.Path, second.Path);

    public bool IsPairAccepted(ContentId content, FileSystemPath first, FileSystemPath second) =>
        acceptedContents.Contains(content) || acceptedPairs.Contains(CanonicalPair(content, first, second));

    public void Clear()
    {
        acceptedContents.Clear();
        acceptedPairs.Clear();
    }

    private static AcceptedPair CanonicalPair(
        ContentId content,
        FileSystemPath first,
        FileSystemPath second) =>
        StringComparer.Ordinal.Compare(first.Value, second.Value) <= 0
            ? new AcceptedPair(content, first, second)
            : new AcceptedPair(content, second, first);

    private readonly record struct AcceptedPair(
        ContentId Content,
        FileSystemPath First,
        FileSystemPath Second);
}
