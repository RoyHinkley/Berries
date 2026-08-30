using Berries.Core;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class ExcludeFilterTests
{
    [Fact]
    public async Task BuildInitialPortraitAsync_ExcludesConfiguredPathsBeforeGroupDiscovery()
    {
        var root = Path(@"X:\Corpus");
        var keep = Path(@"X:\Corpus\keep.txt");
        var excluded = Path(@"X:\Corpus\.git\objects\aa\object");
        var files = new[]
        {
            new FileSystemFile(keep, root, 10),
            new FileSystemFile(excluded, Path(@"X:\Corpus\.git\objects\aa"), 20)
        };

        var engine = new BerriesEngine(new TestFileSystem(root, files));
        var portrait = await engine.BuildInitialPortraitAsync(
            new Corpus(new[] { new CorpusRoot(root) }),
            path => path.Value.Contains(@"\.git\", StringComparison.OrdinalIgnoreCase));

        var file = Assert.Single(portrait.Files);
        Assert.Equal(keep, file.Path);
    }

    private static FileSystemPath Path(string value) => new(value);

    private sealed class TestFileSystem(
        FileSystemPath root,
        IReadOnlyList<FileSystemFile> files) : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) => path;
        public FileSystemPath? GetParentDirectory(FileSystemPath path) => throw UnexpectedCall();
        public IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath requestedRoot) =>
            requestedRoot == root ? files : throw UnexpectedCall();
        public Stream OpenRead(FileSystemPath path) => throw UnexpectedCall();
        public bool Exists(FileSystemPath path) => throw UnexpectedCall();
        public void CreateDirectory(FileSystemPath path) => throw UnexpectedCall();
        public void CopyFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void MoveFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void DeleteFile(FileSystemPath path) => throw UnexpectedCall();
        public void RemoveDirectory(FileSystemPath path) => throw UnexpectedCall();
        public bool PathsEqual(FileSystemPath left, FileSystemPath right) => left == right;
        public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor) => false;
        private static InvalidOperationException UnexpectedCall() => new("Unexpected filesystem call.");
    }
}
