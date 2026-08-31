using System.Text;
using Berries.Core;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class BerriesEngineTests
{
    [Fact]
    public async Task BuildInitialPortraitAsync_UsesSyntheticFileSystem()
    {
        var root = new FileSystemPath(@"X:\Corpus");
        var files = new[]
        {
            new FileSystemFile(
                new FileSystemPath(@"X:\Corpus\alpha.txt"),
                root,
                100,
                new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)),
            new FileSystemFile(
                new FileSystemPath(@"X:\Corpus\Sub\beta.bin"),
                new FileSystemPath(@"X:\Corpus\Sub"),
                250,
                new DateTimeOffset(2026, 8, 2, 13, 0, 0, TimeSpan.Zero))
        };

        var fileSystem = new SyntheticFileSystem(
            new Dictionary<FileSystemPath, IReadOnlyList<FileSystemFile>>
            {
                [root] = files
            });
        var engine = new BerriesEngine(fileSystem);
        var corpus = new Corpus(new[] { new CorpusRoot(root) });

        var portrait = await engine.BuildInitialPortraitAsync(corpus);

        Assert.Collection(
            portrait.Files,
            file =>
            {
                Assert.Equal(files[0].Path, file.Path);
                Assert.Equal(files[0].ParentDirectory, file.ParentDirectory);
                Assert.Equal(files[0].Length, file.Length);
                Assert.Equal(files[0].LastWriteTime, file.LastWriteTime);
            },
            file =>
            {
                Assert.Equal(files[1].Path, file.Path);
                Assert.Equal(files[1].ParentDirectory, file.ParentDirectory);
                Assert.Equal(files[1].Length, file.Length);
                Assert.Equal(files[1].LastWriteTime, file.LastWriteTime);
            });
    }

    [Fact]
    public void CreateCorpus_NormalizesDuplicateAndNestedRoots()
    {
        var fileSystem = new SyntheticFileSystem(
            new Dictionary<FileSystemPath, IReadOnlyList<FileSystemFile>>());
        var engine = new BerriesEngine(fileSystem);

        var corpus = engine.CreateCorpus(new[]
        {
            new FileSystemPath(@"X:\Corpus\Sub"),
            new FileSystemPath(@"Y:\Archive"),
            new FileSystemPath(@"X:\Corpus"),
            new FileSystemPath(@"X:\Corpus\"),
            new FileSystemPath(@"Y:\Archive\Nested")
        });

        Assert.Equal(
            new[] { @"Y:\Archive", @"X:\Corpus" },
            corpus.Roots.Select(root => root.Path.Value));
    }

    [Fact]
    public async Task DiscoverGroupsAsync_HashesOnlyEqualSizeCandidates_AndFindsOnlyEqualContent()
    {
        var root = new FileSystemPath(@"X:\Corpus");
        var paths = new[]
        {
            new FileSystemPath(@"X:\Corpus\a.txt"),
            new FileSystemPath(@"X:\Corpus\b.txt"),
            new FileSystemPath(@"X:\Corpus\c.txt"),
            new FileSystemPath(@"X:\Corpus\d.txt")
        };

        var same = Encoding.UTF8.GetBytes("same bytes");
        var differentSameLength = Encoding.UTF8.GetBytes("other text");
        Assert.Equal(same.Length, differentSameLength.Length);
        var unique = Encoding.UTF8.GetBytes("unique length content");

        var files = new[]
        {
            new FileSystemFile(paths[0], root, same.Length),
            new FileSystemFile(paths[1], root, same.Length),
            new FileSystemFile(paths[2], root, differentSameLength.Length),
            new FileSystemFile(paths[3], root, unique.Length)
        };

        var fileSystem = new SyntheticFileSystem(
            new Dictionary<FileSystemPath, IReadOnlyList<FileSystemFile>>
            {
                [root] = files
            },
            new Dictionary<FileSystemPath, byte[]>
            {
                [paths[0]] = same,
                [paths[1]] = same,
                [paths[2]] = differentSameLength,
                [paths[3]] = unique
            });
        var engine = new BerriesEngine(fileSystem);
        var portrait = await engine.BuildInitialPortraitAsync(new Corpus(new[] { new CorpusRoot(root) }));

        var result = await engine.DiscoverGroupsAsync(portrait);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.FileCount);
        Assert.Equal(new[] { paths[0], paths[1] }, group.Files.Select(file => file.Path));
        Assert.Equal(3, fileSystem.OpenedPaths.Count);
        Assert.DoesNotContain(paths[3], fileSystem.OpenedPaths);
        Assert.Equal(2, result.GroupedFileCount);
        Assert.Empty(result.Evictions);
        Assert.Same(portrait, result.Portrait);
    }

    [Fact]
    public async Task DiscoverGroupsAsync_EvictsFileOnIoFailure_AndContinues()
    {
        var root = new FileSystemPath(@"X:\Corpus");
        var a = new FileSystemPath(@"X:\Corpus\a.txt");
        var busy = new FileSystemPath(@"X:\Corpus\busy.txt");
        var c = new FileSystemPath(@"X:\Corpus\c.txt");
        var content = Encoding.UTF8.GetBytes("same bytes");

        var files = new[]
        {
            new FileSystemFile(a, root, content.Length),
            new FileSystemFile(busy, root, content.Length),
            new FileSystemFile(c, root, content.Length)
        };

        var fileSystem = new SyntheticFileSystem(
            new Dictionary<FileSystemPath, IReadOnlyList<FileSystemFile>>
            {
                [root] = files
            },
            new Dictionary<FileSystemPath, byte[]>
            {
                [a] = content,
                [c] = content
            },
            new Dictionary<FileSystemPath, Exception>
            {
                [busy] = new IOException("File is busy.")
            });
        var engine = new BerriesEngine(fileSystem);
        var portrait = await engine.BuildInitialPortraitAsync(new Corpus(new[] { new CorpusRoot(root) }));

        var result = await engine.DiscoverGroupsAsync(portrait);

        Assert.DoesNotContain(result.Portrait.Files, file => file.Path == busy);
        Assert.Equal(2, result.Portrait.Files.Count);
        var eviction = Assert.Single(result.Evictions);
        Assert.Equal(busy, eviction.File.Path);
        Assert.Contains("busy", eviction.Reason, StringComparison.OrdinalIgnoreCase);
        var group = Assert.Single(result.Groups);
        Assert.Equal(new[] { a, c }, group.Files.Select(file => file.Path));
    }

    [Fact]
    public async Task DiscoverGroupsAsync_DoesNotHideProgrammingErrors()
    {
        var root = new FileSystemPath(@"X:\Corpus");
        var a = new FileSystemPath(@"X:\Corpus\a.txt");
        var b = new FileSystemPath(@"X:\Corpus\b.txt");
        var content = Encoding.UTF8.GetBytes("same bytes");
        var files = new[]
        {
            new FileSystemFile(a, root, content.Length),
            new FileSystemFile(b, root, content.Length)
        };

        var fileSystem = new SyntheticFileSystem(
            new Dictionary<FileSystemPath, IReadOnlyList<FileSystemFile>>
            {
                [root] = files
            },
            new Dictionary<FileSystemPath, byte[]>
            {
                [a] = content
            },
            new Dictionary<FileSystemPath, Exception>
            {
                [b] = new InvalidOperationException("Synthetic programming failure.")
            });
        var engine = new BerriesEngine(fileSystem);
        var portrait = await engine.BuildInitialPortraitAsync(new Corpus(new[] { new CorpusRoot(root) }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.DiscoverGroupsAsync(portrait));
    }

    private sealed class SyntheticFileSystem(
        IReadOnlyDictionary<FileSystemPath, IReadOnlyList<FileSystemFile>> filesByRoot,
        IReadOnlyDictionary<FileSystemPath, byte[]>? contentByPath = null,
        IReadOnlyDictionary<FileSystemPath, Exception>? openFailures = null) : IFileSystem
    {
        private readonly IReadOnlyDictionary<FileSystemPath, byte[]> contentByPath =
            contentByPath ?? new Dictionary<FileSystemPath, byte[]>();
        private readonly IReadOnlyDictionary<FileSystemPath, Exception> openFailures =
            openFailures ?? new Dictionary<FileSystemPath, Exception>();

        public List<FileSystemPath> OpenedPaths { get; } = [];

        public FileSystemPath NormalizePath(FileSystemPath path)
        {
            var value = path.Value.Replace('/', '\\').TrimEnd('\\');
            return new FileSystemPath(value);
        }

        public FileSystemPath? GetParentDirectory(FileSystemPath path)
        {
            var value = NormalizePath(path).Value;
            var separator = value.LastIndexOf('\\');
            if (separator <= 2) return null;
            return new FileSystemPath(value[..separator]);
        }

        public IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath root) =>
            filesByRoot.TryGetValue(root, out var files)
                ? files
                : throw new InvalidOperationException($"Unexpected enumeration root: {root}");

        public Stream OpenRead(FileSystemPath path)
        {
            OpenedPaths.Add(path);
            if (openFailures.TryGetValue(path, out var failure)) throw failure;
            if (!contentByPath.TryGetValue(path, out var content))
                throw new InvalidOperationException($"Unexpected content read: {path}");
            return new MemoryStream(content, writable: false);
        }

        public bool PathsEqual(FileSystemPath left, FileSystemPath right) =>
            StringComparer.OrdinalIgnoreCase.Equals(
                NormalizePath(left).Value,
                NormalizePath(right).Value);

        public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor)
        {
            var child = NormalizePath(candidate).Value;
            var parent = NormalizePath(ancestor).Value;
            if (StringComparer.OrdinalIgnoreCase.Equals(child, parent)) return false;
            return child.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public bool Exists(FileSystemPath path) => throw UnexpectedCall();
        public void CreateDirectory(FileSystemPath path) => throw UnexpectedCall();
        public void CopyFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void MoveFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void DeleteFile(FileSystemPath path) => throw UnexpectedCall();
        public void RemoveDirectory(FileSystemPath path) => throw UnexpectedCall();

        private static InvalidOperationException UnexpectedCall() =>
            new("The test used an unrelated filesystem operation.");
    }
}
