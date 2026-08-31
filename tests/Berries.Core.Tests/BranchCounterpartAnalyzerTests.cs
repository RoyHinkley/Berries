using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;
using Xunit;

namespace Berries.Core.Tests;

public sealed class BranchCounterpartAnalyzerTests
{
    [Fact]
    public void Analyze_SelectsBestPair_NotHighestRankedSeed()
    {
        var root = Path(@"X:\Corpus");
        var highSeed = Path(@"X:\Corpus\HighSeed");
        var lowerSeed = Path(@"X:\Corpus\LowerSeed");
        var weakCounterpart = Path(@"X:\Corpus\WeakCounterpart");
        var strongCounterpart = Path(@"X:\Corpus\StrongCounterpart");

        var branches = new[]
        {
            new BranchRecord(root, null, 884, 116, 5, 116, 58, 5),
            new BranchRecord(highSeed, root, 12, 38, 1, 38, 20, 1),
            new BranchRecord(lowerSeed, root, 30, 20, 1, 20, 15, 1),
            new BranchRecord(weakCounterpart, root, 462, 38, 1, 38, 20, 1),
            new BranchRecord(strongCounterpart, root, 480, 20, 1, 20, 15, 1)
        };

        var groups = new List<Group>();
        AddSharedGroups(groups, highSeed, weakCounterpart, 2, "weak");
        AddInternalGroups(groups, highSeed, 18, "high");
        AddInternalGroups(groups, weakCounterpart, 18, "weak-only");

        AddSharedGroups(groups, lowerSeed, strongCounterpart, 10, "strong");
        AddInternalGroups(groups, lowerSeed, 5, "lower");
        AddInternalGroups(groups, strongCounterpart, 5, "strong-only");

        var analyzer = new BranchCounterpartAnalyzer(new TestFileSystem());
        var result = analyzer.Analyze(
            new Corpus([new CorpusRoot(root)]),
            branches,
            groups,
            [],
            suggestionLimit: 1,
            counterpartLimit: 1);

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(lowerSeed, suggestion.Seed.Branch.Path);
        Assert.Equal(2, suggestion.CandidateSeedRank);
        Assert.Equal(strongCounterpart, Assert.Single(suggestion.Counterparts).Branch.Path);
    }

    private static void AddSharedGroups(ICollection<Group> groups, FileSystemPath first, FileSystemPath second, int count, string prefix)
    {
        for (var i = 0; i < count; i++)
            groups.Add(new Group(new ContentId($"{prefix}-{i}"), [File(first, $"{prefix}-{i}-a"), File(second, $"{prefix}-{i}-b")]));
    }

    private static void AddInternalGroups(ICollection<Group> groups, FileSystemPath branch, int count, string prefix)
    {
        for (var i = 0; i < count; i++)
            groups.Add(new Group(new ContentId($"{prefix}-{i}"), [File(branch, $"{prefix}-{i}-a"), File(branch, $"{prefix}-{i}-b")]));
    }

    private static FileInstance File(FileSystemPath directory, string name) => new(Path(directory.Value + "\\" + name), 10, directory);
    private static FileSystemPath Path(string value) => new(value);

    private sealed class TestFileSystem : IFileSystem
    {
        public FileSystemPath NormalizePath(FileSystemPath path) => new(path.Value.Replace('/', '\\').TrimEnd('\\'));
        public FileSystemPath? GetParentDirectory(FileSystemPath path)
        {
            var value = NormalizePath(path).Value;
            var separator = value.LastIndexOf('\\');
            return separator <= 2 ? null : new FileSystemPath(value[..separator]);
        }
        public bool PathsEqual(FileSystemPath left, FileSystemPath right) => StringComparer.OrdinalIgnoreCase.Equals(NormalizePath(left).Value, NormalizePath(right).Value);
        public bool IsDescendant(FileSystemPath candidate, FileSystemPath ancestor)
        {
            var child = NormalizePath(candidate).Value;
            var parent = NormalizePath(ancestor).Value;
            return !StringComparer.OrdinalIgnoreCase.Equals(child, parent) && child.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
        }
        public IEnumerable<FileSystemFile> EnumerateFiles(FileSystemPath root) => throw UnexpectedCall();
        public Stream OpenRead(FileSystemPath path) => throw UnexpectedCall();
        public bool Exists(FileSystemPath path) => throw UnexpectedCall();
        public void CreateDirectory(FileSystemPath path) => throw UnexpectedCall();
        public void CopyFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void MoveFile(FileSystemPath source, FileSystemPath destination) => throw UnexpectedCall();
        public void DeleteFile(FileSystemPath path) => throw UnexpectedCall();
        public void RemoveDirectory(FileSystemPath path) => throw UnexpectedCall();
        private static InvalidOperationException UnexpectedCall() => new("Branch counterpart analysis unexpectedly performed file I/O.");
    }
}
