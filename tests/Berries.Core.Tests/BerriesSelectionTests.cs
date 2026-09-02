using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class BerriesSelectionTests
{
    [Fact]
    public void SelectedDirectories_DistinguishesZeroOneTwoAndThreeOrMore()
    {
        var a1 = File("/a/1.txt");
        var a2 = File("/a/2.txt");
        var b = File("/b/1.txt");
        var c = File("/c/1.txt");
        var selection = new BerriesSelection(new TestFileSystem(), TestCorpus(), new Portrait([a1, a2, b, c]));

        Assert.True(selection.SelectedDirectories.None);
        Assert.Null(selection.SelectedDirectories.Single);
        Assert.Null(selection.SelectedDirectories.Pair);
        Assert.Null(selection.SelectedDirectories.CommonAncestor);

        selection.Add([a1, a2]);
        Assert.False(selection.SelectedDirectories.None);
        Assert.Equal(new FileSystemPath("/a"), selection.SelectedDirectories.Single);
        Assert.Null(selection.SelectedDirectories.Pair);
        Assert.Equal(new FileSystemPath("/a"), selection.SelectedDirectories.CommonAncestor);

        selection.Add([b]);
        Assert.False(selection.SelectedDirectories.None);
        Assert.Null(selection.SelectedDirectories.Single);
        Assert.True(selection.SelectedDirectories.Pair.HasValue);
        var pair = selection.SelectedDirectories.Pair.Value;
        Assert.Equal(new FileSystemPath("/a"), pair.First);
        Assert.Equal(new FileSystemPath("/b"), pair.Second);
        Assert.Equal(new FileSystemPath("/"), selection.SelectedDirectories.CommonAncestor);

        selection.Add([c]);
        Assert.False(selection.SelectedDirectories.None);
        Assert.Null(selection.SelectedDirectories.Single);
        Assert.Null(selection.SelectedDirectories.Pair);
        Assert.Equal(new FileSystemPath("/"), selection.SelectedDirectories.CommonAncestor);
    }

    [Fact]
    public void SelectedDirectories_CommonAncestorIsDeepestSharedAncestor()
    {
        var first = File("/photos/2024/trips/a.jpg");
        var second = File("/photos/2025/b.jpg");
        var selection = new BerriesSelection(new TestFileSystem(), TestCorpus(), new Portrait([first, second]));

        selection.Add([first, second]);

        Assert.Equal(new FileSystemPath("/photos"), selection.SelectedDirectories.CommonAncestor);
    }

    [Fact]
    public void SelectedDirectories_CommonAncestorDoesNotCrossCorpusRoots()
    {
        var first = File("/first/a/1.txt");
        var second = File("/second/b/1.txt");
        var corpus = new Corpus([
            new CorpusRoot(new FileSystemPath("/first")),
            new CorpusRoot(new FileSystemPath("/second"))
        ]);
        var selection = new BerriesSelection(new TestFileSystem(), corpus, new Portrait([first, second]));

        selection.Add([first, second]);

        Assert.Null(selection.SelectedDirectories.CommonAncestor);
    }

    [Fact]
    public void SelectedDirectoriesChanged_FiresWhenOnlyCommonAncestorChanges()
    {
        var a = File("/root/one/a.txt");
        var b = File("/root/two/b.txt");
        var c = File("/other/three/c.txt");
        var selection = new BerriesSelection(new TestFileSystem(), TestCorpus(), new Portrait([a, b, c]));
        var changes = 0;
        selection.SelectedDirectoriesChanged += (_, _) => changes++;

        selection.Add([a, b]);
        Assert.Equal(1, changes);
        Assert.True(selection.SelectedDirectories.Pair.HasValue);
        Assert.Equal(new FileSystemPath("/root"), selection.SelectedDirectories.CommonAncestor);

        selection.Add([c]);
        Assert.Equal(2, changes);
        Assert.Null(selection.SelectedDirectories.Pair);
        Assert.Equal(new FileSystemPath("/"), selection.SelectedDirectories.CommonAncestor);
    }

    [Fact]
    public void SelectedDirectoriesChanged_FiresOnlyWhenDirectorySummaryChanges()
    {
        var a1 = File("/a/1.txt");
        var a2 = File("/a/2.txt");
        var b = File("/b/1.txt");
        var selection = new BerriesSelection(new TestFileSystem(), TestCorpus(), new Portrait([a1, a2, b]));
        var changes = 0;
        selection.SelectedDirectoriesChanged += (_, _) => changes++;

        selection.Add([a1]);
        Assert.Equal(1, changes);

        selection.Add([a2]);
        Assert.Equal(1, changes);

        selection.Add([b]);
        Assert.Equal(2, changes);

        selection.Remove([a1]);
        Assert.Equal(2, changes);

        selection.Remove([a2]);
        Assert.Equal(3, changes);
        Assert.Equal(new FileSystemPath("/b"), selection.SelectedDirectories.Single);
    }

    private static Corpus TestCorpus() => new([new CorpusRoot(new FileSystemPath("/"))]);

    private static FileInstance File(string path)
    {
        var parent = path[..path.LastIndexOf('/')];
        return new FileInstance(new(path), 10, new(parent), new ContentId(path));
    }

    private sealed class TestFileSystem : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) => new(Normalize(path.Value));

        public FileSystemPath? GetParentDirectory(FileSystemPath path)
        {
            var normalized = Normalize(path.Value);
            if (normalized == "/") return null;
            var separator = normalized.LastIndexOf('/');
            return separator <= 0 ? new FileSystemPath("/") : new FileSystemPath(normalized[..separator]);
        }

        public FileSystemPath GetRelativePath(FileSystemPath relativeTo, FileSystemPath path) => throw Unexpected();
        public FileSystemPath Combine(FileSystemPath directory, FileSystemPath relativePath) => throw Unexpected();
        public bool PathsEqual(FileSystemPath left, FileSystemPath right) => Normalize(left.Value) == Normalize(right.Value);

        public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor)
        {
            var child = Normalize(candidate.Value);
            var parent = Normalize(ancestor.Value);
            if (parent == "/") return child != "/" && child.StartsWith('/');
            return child.Length > parent.Length
                && child.StartsWith(parent, StringComparison.Ordinal)
                && child[parent.Length] == '/';
        }

        public IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath root) => throw Unexpected();
        public Stream OpenRead(FileSystemPath path) => throw Unexpected();
        public bool Exists(FileSystemPath path) => throw Unexpected();
        public void CreateDirectory(FileSystemPath path) => throw Unexpected();
        public void CopyFile(FileSystemPath source, FileSystemPath destination) => throw Unexpected();
        public void MoveFile(FileSystemPath source, FileSystemPath destination) => throw Unexpected();
        public void DeleteFile(FileSystemPath path) => throw Unexpected();
        public void RemoveDirectory(FileSystemPath path) => throw Unexpected();
        private static string Normalize(string value)
        {
            var normalized = value.Replace('\\', '/').TrimEnd('/');
            return normalized.Length == 0 ? "/" : normalized;
        }
        private static InvalidOperationException Unexpected() => new("Unexpected filesystem call.");
    }
}
