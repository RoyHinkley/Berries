using Berries.Core.Domain;
using Berries.Core.Planning;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class SessionTests
{
    private static readonly ContentId A = new("A");
    private static readonly ContentId B = new("B");

    [Fact]
    public void ExcludeChangesPortraitWithoutFilesystemAction()
    {
        var fs = new TestFileSystem();
        var first = File("/old/a.txt", A);
        var second = File("/new/a.txt", A);
        var session = new BerriesSession(fs, TestCorpus(), new Portrait([first, second]));

        session.Exclude([first]);

        Assert.Single(session.WorkingPortrait.Files);
        Assert.Equal(second.Path, session.WorkingPortrait.Files[0].Path);
        Assert.Empty(session.Actions);
        Assert.IsType<ExcludePortraitOperation>(Assert.Single(session.Operations));
    }

    [Fact]
    public void BulkExcludeIsOneUndoStep()
    {
        var fs = new TestFileSystem();
        var first = File("/a.txt", A);
        var second = File("/b.txt", A);
        var third = File("/c.txt", A);
        var session = new BerriesSession(fs, TestCorpus(), new Portrait([first, second, third]));

        session.Exclude([first, second]);

        Assert.Single(session.Operations);
        var batch = Assert.IsType<PortraitOperationBatch>(session.Operations[0]);
        Assert.Equal(2, batch.Operations.Count);
        Assert.Single(session.WorkingPortrait.Files);

        Assert.True(session.Undo());
        Assert.Equal(3, session.WorkingPortrait.Files.Count);
        Assert.Empty(session.Operations);
    }

    [Fact]
    public void DiscoveredGroupMayReachZeroMembers()
    {
        var fs = new TestFileSystem();
        var first = File("/a.txt", A);
        var second = File("/b.txt", A);
        var session = new BerriesSession(fs, TestCorpus(), new Portrait([first, second]));

        session.Exclude([first, second]);

        var group = Assert.Single(session.Groups);
        Assert.Equal(A, group.Content);
        Assert.Empty(group.Files);

        Assert.True(session.Undo());
        Assert.Equal(2, Assert.Single(session.Groups).Files.Count);
    }

    [Fact]
    public void BulkDeleteIsOneUndoStepAndRebuildsActions()
    {
        var fs = new TestFileSystem();
        var first = File("/a.txt", A);
        var second = File("/b.txt", A);
        var third = File("/c.txt", A);
        var session = new BerriesSession(fs, TestCorpus(), new Portrait([first, second, third]));

        session.Delete([first, second]);

        Assert.Single(session.Operations);
        Assert.Equal(2, session.Actions.Count);
        Assert.True(session.Undo());
        Assert.Equal(3, session.WorkingPortrait.Files.Count);
        Assert.Empty(session.Actions);
    }

    [Fact]
    public void MoveDeletesSourceWhenContentAlreadyExistsWithinDestinationDirectory()
    {
        var fs = new TestFileSystem();
        var source = File("/old/trips/vacation.jpg", A);
        var destination = File("/photos/travel/img1234.jpg", A);
        var conflictingName = File("/photos/travel/vacation.jpg", B);
        var session = new BerriesSession(fs, TestCorpus(), new Portrait([source, destination, conflictingName]));

        var result = session.Move([source], new("/old/trips"), new("/photos/travel"));

        Assert.Empty(result.Collisions);
        Assert.DoesNotContain(session.WorkingPortrait.Files, file => file.Path == source.Path);
        Assert.Contains(session.WorkingPortrait.Files, file => file.Path == destination.Path);
        Assert.Contains(session.WorkingPortrait.Files, file => file.Path == conflictingName.Path);
        Assert.IsType<DeleteFileAction>(Assert.Single(session.Actions));
    }

    [Fact]
    public void MoveLeavesDifferentContentPathCollisionUnchanged()
    {
        var fs = new TestFileSystem();
        var source = File("/old/trips/vacation.jpg", A);
        var occupant = File("/photos/travel/vacation.jpg", B);
        var otherA = File("/elsewhere/a.jpg", A);
        var session = new BerriesSession(fs, TestCorpus(), new Portrait([source, occupant, otherA]));

        var result = session.Move([source], new("/old/trips"), new("/photos/travel"));

        Assert.Single(result.Collisions);
        Assert.Contains(session.WorkingPortrait.Files, file => file.Path == source.Path);
        Assert.Contains(session.WorkingPortrait.Files, file => file.Path == occupant.Path);
        Assert.Empty(session.Actions);
    }

    [Fact]
    public void MovePreservesRelativePathAndUndoReconstructsPortrait()
    {
        var fs = new TestFileSystem();
        var source = File("/old/photos/trips/2024/a.jpg", A);
        var otherA = File("/backup/a.jpg", A);
        var session = new BerriesSession(fs, TestCorpus(), new Portrait([source, otherA]));

        var result = session.Move([source], new("/old/photos/trips"), new("/photos/travel"));

        Assert.Empty(result.Collisions);
        Assert.Contains(session.WorkingPortrait.Files, file => file.Path.Value == "/photos/travel/2024/a.jpg");
        var action = Assert.IsType<MoveFileAction>(Assert.Single(session.Actions));
        Assert.Equal("/photos/travel/2024/a.jpg", action.Destination.Value);

        Assert.True(session.Undo());
        Assert.Contains(session.WorkingPortrait.Files, file => file.Path == source.Path);
        Assert.Empty(session.Actions);
    }

    private static Corpus TestCorpus() => new([new CorpusRoot(new FileSystemPath("/"))]);

    private static FileInstance File(string path, ContentId content)
    {
        var parent = path[..path.LastIndexOf('/')];
        return new FileInstance(new(path), 10, new(parent), content);
    }

    private sealed class TestFileSystem : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) => new(Normalize(path.Value));
        public FileSystemPath? GetParentDirectory(FileSystemPath path)
        {
            var value = Normalize(path.Value); var index = value.LastIndexOf('/');
            return index <= 0 ? null : new FileSystemPath(value[..index]);
        }
        public FileSystemPath GetRelativePath(FileSystemPath relativeTo, FileSystemPath path)
        {
            var root = Normalize(relativeTo.Value); var value = Normalize(path.Value);
            if (value == root) return new FileSystemPath("."); return new FileSystemPath(value[(root.Length + 1)..]);
        }
        public FileSystemPath Combine(FileSystemPath directory, FileSystemPath relativePath) => new(Normalize(directory.Value) + "/" + relativePath.Value.TrimStart('/'));
        public bool PathsEqual(FileSystemPath left, FileSystemPath right) => Normalize(left.Value) == Normalize(right.Value);
        public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor) => Normalize(candidate.Value).StartsWith(Normalize(ancestor.Value) + "/", StringComparison.Ordinal);
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
