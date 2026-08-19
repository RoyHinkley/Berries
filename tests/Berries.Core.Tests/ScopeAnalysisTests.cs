using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class ScopeAnalysisTests
{
    [Fact]
    public async Task AnalyzeScopesAsync_AggregatesDescendantDirectoryPairsByDistinctContent()
    {
        var root = Path(@"X:\Corpus");
        var a = Path(@"X:\Corpus\A");
        var b = Path(@"X:\Corpus\B");

        var aOne = File(@"X:\Corpus\A\One", "a1");
        var bOne = File(@"X:\Corpus\B\Uno", "b1");
        var aTwo = File(@"X:\Corpus\A\Two", "a2");
        var bTwo = File(@"X:\Corpus\B\Dos", "b2");

        var duplicateSets = new[]
        {
            new DuplicateSet(new ContentId("01"), new[] { aOne, bOne }),
            new DuplicateSet(new ContentId("02"), new[] { aTwo, bTwo })
        };

        var engine = new BerriesEngine(new TestFileSystem());
        var result = await engine.AnalyzeScopesAsync(
            new Corpus(new[] { new CorpusRoot(root) }),
            duplicateSets);

        var pair = Assert.Single(result.ScopePairs,
            pair => pair.FirstRoot == a && pair.SecondRoot == b);

        Assert.Equal(2, pair.Leverage);
        Assert.Equal(2, pair.DirectoryPairCount);
    }

    [Fact]
    public async Task AnalyzeScopesAsync_NestedRootsExcludeDescendantSubtreeFromAncestorSide()
    {
        var root = Path(@"X:\Corpus");
        var parent = Path(@"X:\Corpus\Parent");
        var inside = Path(@"X:\Corpus\Parent\Inside");

        var outsideFile = File(@"X:\Corpus\Parent\Outside", "outside");
        var insideFile = File(@"X:\Corpus\Parent\Inside\One", "inside");
        var insideA = File(@"X:\Corpus\Parent\Inside\Two", "a");
        var insideB = File(@"X:\Corpus\Parent\Inside\Three", "b");

        var duplicateSets = new[]
        {
            new DuplicateSet(new ContentId("crossing"), new[] { outsideFile, insideFile }),
            new DuplicateSet(new ContentId("internal"), new[] { insideA, insideB })
        };

        var engine = new BerriesEngine(new TestFileSystem());
        var result = await engine.AnalyzeScopesAsync(
            new Corpus(new[] { new CorpusRoot(root) }),
            duplicateSets);

        var pair = Assert.Single(result.ScopePairs,
            pair => pair.FirstRoot == parent && pair.SecondRoot == inside);

        Assert.Equal(1, pair.Leverage);
        Assert.Equal(1, pair.DirectoryPairCount);
    }

    private static FileInstance File(string directory, string name) =>
        new(Path(directory + "\\" + name), 10, Path(directory));

    private static FileSystemPath Path(string value) => new(value);

    private sealed class TestFileSystem : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) =>
            new(path.Value.Replace('/', '\\').TrimEnd('\\'));

        public FileSystemPath? GetParentDirectory(FileSystemPath path)
        {
            var value = NormalizePath(path).Value;
            var separator = value.LastIndexOf('\\');
            if (separator <= 2)
                return null;

            return new FileSystemPath(value[..separator]);
        }

        public bool PathsEqual(FileSystemPath left, FileSystemPath right) =>
            StringComparer.OrdinalIgnoreCase.Equals(
                NormalizePath(left).Value,
                NormalizePath(right).Value);

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
            new("Scope analysis unexpectedly performed file I/O.");
    }
}
