using Berries.Core.Analysis;
using Berries.Core.Cases;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class CaseAnalysisTests
{
    [Fact]
    public void AnalyzeTop_BuildsAllCaseTypes_AndReportsPopulationCounts()
    {
        var a = Path(@"X:\Corpus\A");
        var b = Path(@"X:\Corpus\B");
        var a1 = File(a, "a1");
        var a2 = File(a, "a2");
        var b1 = File(b, "b1");
        var unique = File(a, "unique");
        var portrait = new Portrait(new[] { a1, a2, b1, unique });

        var duplicateSets = new[]
        {
            new DuplicateSet(new ContentId("01"), new[] { a1, a2, b1 })
        };
        var directoryPairs = new[] { new DirectoryPair(a, b, 1) };
        var scopePairs = new[] { new ScopePair(a, b, 1, 1) };

        var result = new CaseAnalyzer(new TestFileSystem()).AnalyzeTop(
            portrait,
            duplicateSets,
            directoryPairs,
            scopePairs,
            25);

        Assert.Equal(4, result.TotalCaseCount);
        Assert.Equal(4, result.TopCases.Count);
        Assert.Equal(1, result.DuplicateSetCaseCount);
        Assert.Equal(1, result.SingleDirectoryCaseCount);
        Assert.Equal(1, result.DirectoryPairCaseCount);
        Assert.Equal(1, result.ScopePairCaseCount);
        Assert.All(result.TopCases, item => Assert.Equal(1, item.Leverage));

        var directoryCase = Assert.Single(result.TopCases.OfType<SingleDirectoryCase>());
        Assert.Contains(unique, directoryCase.Files);

        var scopeCase = Assert.Single(result.TopCases.OfType<ScopePairCase>());
        Assert.Equal(4, scopeCase.Files.Count);
    }

    [Fact]
    public void AnalyzeTop_NestedScopePairBoundsDisjointEffectiveSides()
    {
        var parent = Path(@"X:\Corpus\Parent");
        var child = Path(@"X:\Corpus\Parent\Child");
        var outside = File(parent, "outside");
        var inside = File(child, "inside");
        var deeper = File(Path(@"X:\Corpus\Parent\Child\Deep"), "deeper");
        var portrait = new Portrait(new[] { outside, inside, deeper });

        var result = new CaseAnalyzer(new TestFileSystem()).AnalyzeTop(
            portrait,
            Array.Empty<DuplicateSet>(),
            Array.Empty<DirectoryPair>(),
            new[] { new ScopePair(parent, child, 1, 1) },
            25);

        var scopeCase = Assert.Single(result.TopCases.OfType<ScopePairCase>());
        Assert.Equal(3, scopeCase.Files.Count);
        Assert.Equal(3, scopeCase.Files.Distinct().Count());
    }

    [Fact]
    public void AnalyzeTop_MaterializesOnlyRequestedSample()
    {
        var a = Path(@"X:\Corpus\A");
        var b = Path(@"X:\Corpus\B");
        var portrait = new Portrait(new[] { File(a, "a"), File(b, "b") });
        var pairs = Enumerable.Range(1, 100)
            .Select(index => new ScopePair(a, b, index, 1))
            .ToArray();

        var result = new CaseAnalyzer(new TestFileSystem()).AnalyzeTop(
            portrait,
            Array.Empty<DuplicateSet>(),
            Array.Empty<DirectoryPair>(),
            pairs,
            25);

        Assert.Equal(100, result.TotalCaseCount);
        Assert.Equal(25, result.TopCases.Count);
        Assert.Equal(100, result.TopCases[0].Leverage);
        Assert.Equal(76, result.TopCases[^1].Leverage);
    }

    private static FileInstance File(FileSystemPath directory, string name) =>
        new(Path(directory.Value + "\\" + name), 10, directory);

    private static FileSystemPath Path(string value) => new(value);

    private sealed class TestFileSystem : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) =>
            new(path.Value.Replace('/', '\\').TrimEnd('\\'));

        public FileSystemPath? GetParentDirectory(FileSystemPath path) => throw UnexpectedCall();

        public bool PathsEqual(FileSystemPath left, FileSystemPath right) =>
            StringComparer.OrdinalIgnoreCase.Equals(NormalizePath(left).Value, NormalizePath(right).Value);

        public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor)
        {
            var child = NormalizePath(candidate).Value;
            var parent = NormalizePath(ancestor).Value;
            return !StringComparer.OrdinalIgnoreCase.Equals(child, parent)
                && child.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath root) => throw UnexpectedCall();
        public Stream OpenRead(FileSystemPath path) => throw UnexpectedCall();
        public bool Exists(FileSystemPath path) => throw UnexpectedCall();
        public void CreateDirectory(FileSystemPath path) => throw UnexpectedCall();
        public void CopyFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void MoveFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void DeleteFile(FileSystemPath path) => throw UnexpectedCall();
        public void RemoveDirectory(FileSystemPath path) => throw UnexpectedCall();

        private static InvalidOperationException UnexpectedCall() =>
            new("Case analysis unexpectedly performed file I/O.");
    }
}
