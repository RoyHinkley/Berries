using Berries.Core.Analysis;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class ScopeAnalysisTests
{
    [Fact]
    public async Task AnalyzeScopesAsync_AggregatesDescendantDirectoryPairEvidence()
    {
        var root = Path(@"X:\Corpus");
        var a = Path(@"X:\Corpus\A");
        var b = Path(@"X:\Corpus\B");

        var directoryPairs = new[]
        {
            new DirectoryPair(Path(@"X:\Corpus\A\One"), Path(@"X:\Corpus\B\Uno"), 1),
            new DirectoryPair(Path(@"X:\Corpus\A\Two"), Path(@"X:\Corpus\B\Dos"), 1)
        };

        var engine = new BerriesEngine(new TestFileSystem());
        var result = await engine.AnalyzeScopesAsync(
            new Corpus(new[] { new CorpusRoot(root) }),
            directoryPairs);

        var pair = Assert.Single(result.ScopePairs,
            pair => pair.FirstRoot == a && pair.SecondRoot == b);

        Assert.Equal(2, pair.Leverage);
        Assert.Equal(2, pair.DirectoryPairCount);
    }

    [Fact]
    public async Task AnalyzeScopesAsync_UsesDirectoryPairWeights()
    {
        var root = Path(@"X:\Corpus");
        var a = Path(@"X:\Corpus\A");
        var b = Path(@"X:\Corpus\B");

        var directoryPairs = new[]
        {
            new DirectoryPair(Path(@"X:\Corpus\A\One"), Path(@"X:\Corpus\B\One"), 2),
            new DirectoryPair(Path(@"X:\Corpus\A\Two"), Path(@"X:\Corpus\B\One"), 1)
        };

        var engine = new BerriesEngine(new TestFileSystem());
        var result = await engine.AnalyzeScopesAsync(
            new Corpus(new[] { new CorpusRoot(root) }),
            directoryPairs);

        var pair = Assert.Single(result.ScopePairs,
            pair => pair.FirstRoot == a && pair.SecondRoot == b);

        Assert.Equal(3, pair.Leverage);
        Assert.Equal(2, pair.DirectoryPairCount);
    }

    [Fact]
    public async Task AnalyzeScopesAsync_NestedRootsExcludeDescendantSubtreeFromAncestorSide()
    {
        var root = Path(@"X:\Corpus");
        var parent = Path(@"X:\Corpus\Parent");
        var inside = Path(@"X:\Corpus\Parent\Inside");

        var directoryPairs = new[]
        {
            new DirectoryPair(
                Path(@"X:\Corpus\Parent\Outside"),
                Path(@"X:\Corpus\Parent\Inside\One"),
                1),
            new DirectoryPair(
                Path(@"X:\Corpus\Parent\Inside\Two"),
                Path(@"X:\Corpus\Parent\Inside\Three"),
                1)
        };

        var engine = new BerriesEngine(new TestFileSystem());
        var result = await engine.AnalyzeScopesAsync(
            new Corpus(new[] { new CorpusRoot(root) }),
            directoryPairs);

        var pair = Assert.Single(result.ScopePairs,
            pair => pair.FirstRoot == parent && pair.SecondRoot == inside);

        Assert.Equal(1, pair.Leverage);
        Assert.Equal(1, pair.DirectoryPairCount);
    }

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
