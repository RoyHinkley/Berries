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
        var selection = new BerriesSelection(new TestFileSystem(), new Portrait([a1, a2, b, c]));

        Assert.True(selection.SelectedDirectories.NoDir);
        Assert.Null(selection.SelectedDirectories.OneDir);
        Assert.Null(selection.SelectedDirectories.DirPair);

        selection.Add([a1, a2]);
        Assert.False(selection.SelectedDirectories.NoDir);
        Assert.Equal(new FileSystemPath("/a"), selection.SelectedDirectories.OneDir);
        Assert.Null(selection.SelectedDirectories.DirPair);

        selection.Add([b]);
        Assert.False(selection.SelectedDirectories.NoDir);
        Assert.Null(selection.SelectedDirectories.OneDir);
        var pair = Assert.IsType<(FileSystemPath First, FileSystemPath Second)>(selection.SelectedDirectories.DirPair);
        Assert.Equal(new FileSystemPath("/a"), pair.First);
        Assert.Equal(new FileSystemPath("/b"), pair.Second);

        selection.Add([c]);
        Assert.False(selection.SelectedDirectories.NoDir);
        Assert.Null(selection.SelectedDirectories.OneDir);
        Assert.Null(selection.SelectedDirectories.DirPair);
    }

    [Fact]
    public void SelectedDirectoriesChanged_FiresOnlyWhenDirectorySummaryChanges()
    {
        var a1 = File("/a/1.txt");
        var a2 = File("/a/2.txt");
        var b = File("/b/1.txt");
        var selection = new BerriesSelection(new TestFileSystem(), new Portrait([a1, a2, b]));
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
        Assert.Equal(new FileSystemPath("/b"), selection.SelectedDirectories.OneDir);
    }

    private static FileInstance File(string path)
    {
        var parent = path[..path.LastIndexOf('/')];
        return new FileInstance(new(path), 10, new(parent), new ContentId(path));
    }

    private sealed class TestFileSystem : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) => new(Normalize(path.Value));
        public FileSystemPath? GetParentDirectory(FileSystemPath path) => throw Unexpected();
        public FileSystemPath GetRelativePath(FileSystemPath relativeTo, FileSystemPath path) => throw Unexpected();
        public FileSystemPath Combine(FileSystemPath directory, FileSystemPath relativePath) => throw Unexpected();
        public bool PathsEqual(FileSystemPath left, FileSystemPath right) => Normalize(left.Value) == Normalize(right.Value);
        public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor) => throw Unexpected();
        public IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath root) => throw Unexpected();
        public Stream OpenRead(FileSystemPath path) => throw Unexpected();
        public bool Exists(FileSystemPath path) => throw Unexpected();
        public void CreateDirectory(FileSystemPath path) => throw Unexpected();
        public void CopyFile(FileSystemPath source, FileSystemPath destination) => throw Unexpected();
        public void MoveFile(FileSystemPath source, FileSystemPath destination) => throw Unexpected();
        public void DeleteFile(FileSystemPath path) => throw Unexpected();
        public void RemoveDirectory(FileSystemPath path) => throw Unexpected();
        private static string Normalize(string value) => value.Replace('\\', '/').TrimEnd('/');
        private static InvalidOperationException Unexpected() => new("Unexpected filesystem call.");
    }
}
