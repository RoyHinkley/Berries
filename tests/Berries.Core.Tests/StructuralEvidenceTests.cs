using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class StructuralEvidenceTests
{
    [Fact]
    public void AnalyzeScopePair_FindsContributingPairs_Coverage_Subsidiaries_AndBreadth()
    {
        var fs = new TestFileSystem();
        var analyzer = new StructuralEvidenceAnalyzer(fs);
        var a = Path(@"X:\Corpus\A"); var b = Path(@"X:\Corpus\B");
        var a1 = Path(@"X:\Corpus\A\One"); var a2 = Path(@"X:\Corpus\A\Two");
        var b1 = Path(@"X:\Corpus\B\One"); var b2 = Path(@"X:\Corpus\B\Two");
        var fA1 = File(a1, "a1"); var fB1 = File(b1, "b1"); var fA2 = File(a2, "a2"); var fB2 = File(b2, "b2"); var onlyA = File(a, "only-a");
        var onlyACopy = File(a, "only-a-copy");
        var duplicateSets = new[] {
            new DuplicateSet(new ContentId("01"), new[] { fA1, fB1 }),
            new DuplicateSet(new ContentId("02"), new[] { fA2, fB2 }),
            new DuplicateSet(new ContentId("03"), new[] { onlyA, onlyACopy }) };
        var portraitFiles = new[] { fA1, fB1, fA2, fB2, onlyA, onlyACopy };
        var directoryPairs = new[] { new DirectoryPair(a1, b1, 1), new DirectoryPair(a2, b2, 1) };
        var parent = new ScopePair(a, b, 2, 2); var child = new ScopePair(a1, b1, 1, 1); var other = new ScopePair(a1, a2, 1, 1);
        var evidence = analyzer.AnalyzeScopePair(parent, portraitFiles, duplicateSets, directoryPairs, new[] { parent, child, other });

        Assert.False(evidence.RootsNested);
        Assert.Equal(3, evidence.FirstSideDuplicateContentCount); Assert.Equal(2, evidence.SecondSideDuplicateContentCount);
        Assert.Equal(3, evidence.FirstSideBreadth.DirectoryCount); Assert.Equal(4, evidence.FirstSideBreadth.FileCount);
        Assert.Equal(2, evidence.SecondSideBreadth.DirectoryCount); Assert.Equal(2, evidence.SecondSideBreadth.FileCount);
        Assert.Equal(2, evidence.FirstSideBreadth.CrossingDirectoryCount); Assert.Equal(2, evidence.SecondSideBreadth.CrossingDirectoryCount);
        Assert.Equal(1, evidence.SubsidiaryScopePairCount);
        var childSummary = Assert.Single(evidence.StrongestSubsidiaryScopePairs);
        Assert.Equal(child, childSummary.Pair);
        Assert.Equal(1, childSummary.FirstSideBreadth.DirectoryCount); Assert.Equal(1, childSummary.FirstSideBreadth.FileCount);
        Assert.Equal(1, childSummary.SecondSideBreadth.DirectoryCount); Assert.Equal(1, childSummary.SecondSideBreadth.FileCount);
        Assert.Equal(1, childSummary.FirstRootDepthChange); Assert.Equal(1, childSummary.SecondRootDepthChange);
        Assert.Equal(2, evidence.StrongestContributingDirectoryPairs.Count);
        Assert.Equal(0.5, evidence.StrongestDirectoryPairFraction);
        Assert.Equal(1.0, evidence.TopFiveDirectoryPairFraction);
    }

    [Fact]
    public void AnalyzeScopePair_RecognizesNestedRoots_AndExcludesDescendantSubtreeFromAncestorSide()
    {
        var analyzer = new StructuralEvidenceAnalyzer(new TestFileSystem());
        var parent = Path(@"X:\Corpus\Parent"); var child = Path(@"X:\Corpus\Parent\Child");
        var outside = Path(@"X:\Corpus\Parent\Outside"); var inside = Path(@"X:\Corpus\Parent\Child\Inside");
        var crossing = new DuplicateSet(new ContentId("cross"), new[] { File(outside, "a"), File(inside, "b") });
        var internalChild = new DuplicateSet(new ContentId("internal"), new[] { File(inside, "c"), File(inside, "d") });
        var portraitFiles = crossing.Files.Concat(internalChild.Files).ToArray();
        var pair = new ScopePair(parent, child, 1, 1);
        var evidence = analyzer.AnalyzeScopePair(pair, portraitFiles, new[] { crossing, internalChild }, new[] { new DirectoryPair(outside, inside, 1) }, new[] { pair });

        Assert.True(evidence.RootsNested); Assert.Equal(1, evidence.FirstSideDuplicateContentCount); Assert.Equal(2, evidence.SecondSideDuplicateContentCount);
        Assert.Equal(1, evidence.FirstSideBreadth.DirectoryCount); Assert.Equal(1, evidence.FirstSideBreadth.FileCount);
        Assert.Equal(1, evidence.SecondSideBreadth.DirectoryCount); Assert.Equal(3, evidence.SecondSideBreadth.FileCount);
        Assert.Equal(1, evidence.FirstSideBreadth.CrossingDirectoryCount); Assert.Equal(1, evidence.SecondSideBreadth.CrossingDirectoryCount);
        Assert.Empty(evidence.StrongestSubsidiaryScopePairs); Assert.Single(evidence.StrongestContributingDirectoryPairs);
    }

    private static FileInstance File(FileSystemPath directory, string name) => new(Path(directory.Value + "\\" + name), 10, directory);
    private static FileSystemPath Path(string value) => new(value);

    private sealed class TestFileSystem : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) => new(path.Value.Replace('/', '\\').TrimEnd('\\'));

        public FileSystemPath? GetParentDirectory(FileSystemPath path)
        {
            var value = NormalizePath(path).Value;
            var index = value.LastIndexOf('\\');
            if (index <= 2)
                return null;
            return new FileSystemPath(value[..index]);
        }

        public bool PathsEqual(FileSystemPath left, FileSystemPath right) => StringComparer.OrdinalIgnoreCase.Equals(NormalizePath(left).Value, NormalizePath(right).Value);
        public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor) { var child = NormalizePath(candidate).Value; var parent = NormalizePath(ancestor).Value; return !StringComparer.OrdinalIgnoreCase.Equals(child, parent) && child.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase); }
        public IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath root) => throw UnexpectedCall(); public Stream OpenRead(FileSystemPath path) => throw UnexpectedCall(); public bool Exists(FileSystemPath path) => throw UnexpectedCall(); public void CreateDirectory(FileSystemPath path) => throw UnexpectedCall(); public void CopyFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall(); public void MoveFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall(); public void DeleteFile(FileSystemPath path) => throw UnexpectedCall(); public void RemoveDirectory(FileSystemPath path) => throw UnexpectedCall();
        private static InvalidOperationException UnexpectedCall() => new("Structural evidence analysis unexpectedly performed file I/O.");
    }
}
