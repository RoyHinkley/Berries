using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class BranchStatisticsTests
{
    [Fact]
    public void Analyze_AggregatesDistinctGroupsAcrossDescendants()
    {
        var root = Path(@"X:\Corpus");
        var a = Path(@"X:\Corpus\A");
        var a1 = Path(@"X:\Corpus\A\One");
        var a2 = Path(@"X:\Corpus\A\Two");
        var b = Path(@"X:\Corpus\B");

        var first = new Group(
            new ContentId("first"),
            new[] { File(a1, "f1"), File(a2, "f2"), File(b, "f3") });
        var second = new Group(
            new ContentId("second"),
            new[] { File(a1, "g1"), File(b, "g2") });
        var groups = new[] { first, second };
        var portrait = new Portrait(groups.SelectMany(group => group.Files).ToArray());

        var directories = new[]
        {
            new DirectoryRecord(a1, 2, 2, 2),
            new DirectoryRecord(a2, 1, 1, 1),
            new DirectoryRecord(b, 2, 2, 2)
        };

        var analyzer = new BranchStatisticsAnalyzer(new TestFileSystem());
        var result = analyzer.Analyze(
            new Corpus(new[] { new CorpusRoot(root) }),
            portrait,
            groups,
            directories);

        var branch = Assert.Single(result.Branches, item => item.Path == a);
        Assert.Equal(3, branch.FileCount);
        Assert.Equal(2, branch.DirectoryCount);
        Assert.Equal(3, branch.GroupedFileCount);
        Assert.Equal(2, branch.GroupCount);
        Assert.Equal(2, branch.GroupedDirectoryCount);
    }

    private static FileInstance File(FileSystemPath directory, string name) =>
        new(Path(directory.Value + "\\" + name), 10, directory);

    private static FileSystemPath Path(string value) => new(value);

    private sealed class TestFileSystem : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) =>
            new(path.Value.Replace('/', '\\').TrimEnd('\\'));

        public FileSystemPath? GetParentDirectory(FileSystemPath path)
        {
            var value = NormalizePath(path).Value;
            var separator = value.LastIndexOf('\\');
            if (separator <= 2) return null;
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
            new("Branch statistics unexpectedly performed file I/O.");
    }
}
