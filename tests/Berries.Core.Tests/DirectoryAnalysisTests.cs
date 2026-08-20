using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class DirectoryAnalysisTests
{
    [Fact]
    public async Task AnalyzeDirectoriesAsync_BuildsDirectRecords_AndRanksPairsByDistinctSharedContent()
    {
        var directoryA = new FileSystemPath(@"X:\Corpus\A");
        var directoryB = new FileSystemPath(@"X:\Corpus\B");
        var directoryC = new FileSystemPath(@"X:\Corpus\C");

        FileInstance File(string directory, string name) =>
            new(new FileSystemPath($@"X:\Corpus\{directory}\{name}"), 10,
                new FileSystemPath($@"X:\Corpus\{directory}"));

        var a1 = File("A", "a1");
        var a2 = File("A", "a2");
        var a3 = File("A", "a3");
        var a3Copy = File("A", "a3-copy");
        var aUnique = File("A", "unique");
        var b1 = File("B", "b1");
        var b2 = File("B", "b2");
        var b2Copy = File("B", "b2-copy");
        var c1 = File("C", "c1");

        var portrait = new Portrait(new[]
        {
            a1, a2, a3, a3Copy, aUnique,
            b1, b2, b2Copy,
            c1
        });

        var content1 = new ContentId("01");
        var content2 = new ContentId("02");
        var content3 = new ContentId("03");
        var duplicateSets = new[]
        {
            new DuplicateSet(content1, new[] { a1, b1, c1 }),
            new DuplicateSet(content2, new[] { a2, b2, b2Copy }),
            new DuplicateSet(content3, new[] { a3, a3Copy })
        };

        var engine = new BerriesEngine(new UnusedFileSystem());
        var result = await engine.AnalyzeDirectoriesAsync(portrait, duplicateSets);

        Assert.Equal(3, result.Directories.Count);

        var recordA = Assert.Single(result.Directories, record => record.Path == directoryA);
        Assert.Equal(5, recordA.FileCount);
        Assert.Equal(4, recordA.DuplicateFileCount);
        Assert.Equal(3, recordA.DuplicateContentCount);

        var recordB = Assert.Single(result.Directories, record => record.Path == directoryB);
        Assert.Equal(3, recordB.FileCount);
        Assert.Equal(3, recordB.DuplicateFileCount);
        Assert.Equal(2, recordB.DuplicateContentCount);

        var recordC = Assert.Single(result.Directories, record => record.Path == directoryC);
        Assert.Equal(1, recordC.FileCount);
        Assert.Equal(1, recordC.DuplicateFileCount);
        Assert.Equal(1, recordC.DuplicateContentCount);

        Assert.Equal(3, result.DirectoryPairs.Count);
        Assert.Equal(2, result.DirectoryPairs[0].Leverage);
        Assert.Equal(directoryA, result.DirectoryPairs[0].First);
        Assert.Equal(directoryB, result.DirectoryPairs[0].Second);
        Assert.All(result.DirectoryPairs.Skip(1), pair => Assert.Equal(1, pair.Leverage));

        Assert.Equal(3, result.Graph.TotalDirectoryCount);
        Assert.Equal(3, result.Graph.DuplicateDirectoryCount);
        Assert.Equal(2, result.Graph.InternalDuplicateDirectoryCount);
        Assert.Equal(3, result.Graph.PairParticipatingDirectoryCount);
        Assert.Equal(3, result.Graph.DirectoryPairCount);
        Assert.Equal(1, result.Graph.ConnectedComponentCount);
        Assert.Equal(3, result.Graph.LargestComponentSize);
        Assert.Equal(1.0, result.Graph.PairDensity);

        var nodeA = Assert.Single(result.Graph.Nodes, node => node.Directory == directoryA);
        Assert.Equal(2, nodeA.Degree);
        Assert.Equal(3, nodeA.WeightedDegree);
        Assert.Equal(2, nodeA.MaxPairLeverage);
        Assert.Equal(1.5, nodeA.MeanPairLeverage);
        Assert.Equal(2.0 / 3.0, nodeA.StrongestPairConcentration, 10);
    }

    [Fact]
    public async Task AnalyzeDirectoriesAsync_WholeDuplicateSetSettlement_RemovesItsEvidence()
    {
        FileInstance File(string directory, string name) =>
            new(new FileSystemPath($@"X:\Corpus\{directory}\{name}"), 10,
                new FileSystemPath($@"X:\Corpus\{directory}"));

        var a1 = File("A", "shared");
        var b1 = File("B", "shared");
        var c1 = File("C", "shared");
        var a2 = File("A", "other");
        var b2 = File("B", "other");
        var acceptedContent = new DuplicateSet(new ContentId("accepted"), new[] { a1, b1, c1 });
        var unresolvedContent = new DuplicateSet(new ContentId("unresolved"), new[] { a2, b2 });
        var portrait = new Portrait(new[] { a1, b1, c1, a2, b2 });
        var settlements = new DuplicateSettlements();
        settlements.Accept(acceptedContent);

        var engine = new BerriesEngine(new UnusedFileSystem());
        var result = await engine.AnalyzeDirectoriesAsync(
            portrait,
            new[] { acceptedContent, unresolvedContent },
            settlements);

        Assert.Equal(2, result.Directories.Count);
        Assert.DoesNotContain(result.Directories, record => record.Path == c1.ParentDirectory);
        var pair = Assert.Single(result.DirectoryPairs);
        Assert.Equal(a1.ParentDirectory, pair.First);
        Assert.Equal(b1.ParentDirectory, pair.Second);
        Assert.Equal(1, pair.Leverage);
    }

    [Fact]
    public async Task AnalyzeDirectoriesAsync_PairwiseSettlement_RemovesOnlyThatRelationship()
    {
        FileInstance File(string directory, string name) =>
            new(new FileSystemPath($@"X:\Corpus\{directory}\{name}"), 10,
                new FileSystemPath($@"X:\Corpus\{directory}"));

        var a = File("A", "same");
        var b = File("B", "same");
        var c = File("C", "same");
        var content = new ContentId("same-content");
        var duplicateSet = new DuplicateSet(content, new[] { a, b, c });
        var settlements = new DuplicateSettlements();
        settlements.AcceptPair(content, a, b);

        var engine = new BerriesEngine(new UnusedFileSystem());
        var result = await engine.AnalyzeDirectoriesAsync(
            new Portrait(new[] { a, b, c }),
            new[] { duplicateSet },
            settlements);

        Assert.Equal(3, result.Directories.Count);
        Assert.Equal(2, result.DirectoryPairs.Count);
        Assert.DoesNotContain(result.DirectoryPairs,
            pair => (pair.First == a.ParentDirectory && pair.Second == b.ParentDirectory)
                || (pair.First == b.ParentDirectory && pair.Second == a.ParentDirectory));
        Assert.Contains(result.DirectoryPairs,
            pair => pair.First == a.ParentDirectory && pair.Second == c.ParentDirectory);
        Assert.Contains(result.DirectoryPairs,
            pair => pair.First == b.ParentDirectory && pair.Second == c.ParentDirectory);
    }

    private sealed class UnusedFileSystem : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) => throw UnexpectedCall();
        public FileSystemPath? GetParentDirectory(FileSystemPath path) => throw UnexpectedCall();
        public IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath root) => throw UnexpectedCall();
        public Stream OpenRead(FileSystemPath path) => throw UnexpectedCall();
        public bool Exists(FileSystemPath path) => throw UnexpectedCall();
        public void CreateDirectory(FileSystemPath path) => throw UnexpectedCall();
        public void CopyFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void MoveFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void DeleteFile(FileSystemPath path) => throw UnexpectedCall();
        public void RemoveDirectory(FileSystemPath path) => throw UnexpectedCall();
        public bool PathsEqual(FileSystemPath left, FileSystemPath right) => throw UnexpectedCall();
        public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor) => throw UnexpectedCall();

        private static InvalidOperationException UnexpectedCall() =>
            new("Directory analysis unexpectedly used the filesystem abstraction.");
    }
}
