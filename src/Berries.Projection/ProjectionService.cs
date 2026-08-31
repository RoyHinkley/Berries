using Berries.Core;
using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.Core.Queries;
using Berries.FileSystem.Abstractions;

namespace Berries.Projection;

public sealed class ProjectionService(PortraitQueries queries)
{
    public async Task<DirectoryProjection> DirectoryAsync(
        BerriesSession session,
        FileSystemPath directory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = await queries.GroupedFilesInDirectoryAsync(session, directory, progress, cancellationToken);
        return new DirectoryProjection(directory,
            files.Select(file => new DirectoryProjectionFile(Path.GetFileName(file.Path.Value), file)).ToArray());
    }

    public async Task<BranchProjection> BranchAsync(
        BerriesSession session,
        FileSystemPath branch,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var placements = await queries.GroupedFilesInBranchWithPlacementAsync(session, branch, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return BuildBranch(branch, placements, cancellationToken);
    }

    public async Task<IReadOnlyList<BranchProjection>> CorpusRootsAsync(
        BerriesSession session,
        Corpus corpus,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var roots = await queries.GroupedFilesInCorpusRootsWithPlacementAsync(session, corpus, progress, cancellationToken);
        return roots.Select(root => BuildBranch(root.Root, root.Files, cancellationToken)).ToArray();
    }

    public Task<IReadOnlyList<GroupProjection>> GroupsAsync(
        BerriesSession session,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        BuildGroupsAsync(session, null, "Building Groups view", progress, cancellationToken);

    public Task<IReadOnlyList<GroupProjection>> GroupsForSelectionAsync(
        BerriesSession session,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        BuildGroupsAsync(
            session,
            session.Selection.Files
                .Where(file => file.Content is not null)
                .Select(file => file.Content!.Value)
                .ToHashSet(),
            "Building selected Groups view",
            progress,
            cancellationToken);

    public GroupProjection Group(IReadOnlyList<FileInstance> files)
    {
        var names = files.Select(file => Path.GetFileName(file.Path.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var shownNames = string.Join(", ", names.Take(2));
        if (names.Length > 2) shownNames += ", …";
        return new GroupProjection(
            $"{shownNames} — {files.Count:N0} files",
            files,
            files.OrderBy(file => file.Path.Value, StringComparer.OrdinalIgnoreCase)
                .Select(file => new GroupProjectionFile(file.Path.Value, file)).ToArray());
    }

    public IReadOnlyList<FileInstance> DistinctFiles(IEnumerable<FileInstance> files) =>
        queries.DistinctFiles(files);

    public IReadOnlyList<FileInstance> FilesInContext(
        IEnumerable<FileInstance> files,
        FileSystemPath context,
        bool includeDescendants) =>
        queries.FilesInContext(files, context, includeDescendants);

    public IReadOnlyList<FileInstance> SelectedFilesInContext(
        BerriesSession session,
        FileSystemPath context,
        bool includeDescendants) =>
        queries.SelectedFilesInContext(session, context, includeDescendants);

    public bool CorpusRootsMatch(Corpus corpus, IEnumerable<FileSystemPath> roots) =>
        queries.CorpusRootsMatch(corpus, roots);

    public IReadOnlyList<FileSystemPath> Breadcrumbs(Corpus corpus, FileSystemPath path) =>
        queries.AncestorsWithinCorpus(corpus, path);

    public DirectoryRecord? DirectoryRecord(
        IReadOnlyList<DirectoryRecord> directories,
        FileSystemPath directory) =>
        queries.DirectoryRecord(directories, directory);

    public DirectoryPair? BestDirectoryPair(
        IReadOnlyList<DirectoryPair> pairs,
        FileSystemPath directory) =>
        queries.BestDirectoryPair(pairs, directory);

    public bool HasBranchPairCandidate(
        IReadOnlyList<BranchRecord> branches,
        FileSystemPath branch) =>
        queries.HasBranchPairCandidate(branches, branch);

    public Task<int> SharedGroupCountAsync(
        BerriesSession session,
        FileSystemPath first,
        FileSystemPath second,
        bool includeDescendants,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        queries.SharedGroupCountAsync(session, first, second, includeDescendants, progress, cancellationToken);

    private Task<IReadOnlyList<GroupProjection>> BuildGroupsAsync(
        BerriesSession session,
        IReadOnlySet<ContentId>? selectedContents,
        string phase,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<GroupProjection>>(() =>
        {
            var source = session.Groups;
            var groups = new List<Group>(source.Count);
            progress?.Report(new OperationProgress(phase, 0, source.Count));

            for (var i = 0; i < source.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var group = source[i];
                if (group.Files.Count > 0
                    && (selectedContents is null || selectedContents.Contains(group.Content)))
                    groups.Add(group);
                if ((i & 0xff) == 0 || i + 1 == source.Count)
                    progress?.Report(new OperationProgress(phase, i + 1, source.Count));
            }

            cancellationToken.ThrowIfCancellationRequested();
            groups.Sort((left, right) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var byCount = right.Files.Count.CompareTo(left.Files.Count);
                if (byCount != 0) return byCount;
                return StringComparer.OrdinalIgnoreCase.Compare(
                    left.Files[0].Path.Value,
                    right.Files[0].Path.Value);
            });

            var result = new GroupProjection[groups.Count];
            progress?.Report(new OperationProgress(phase, 0, groups.Count));
            for (var i = 0; i < groups.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result[i] = Group(groups[i].Files);
                if ((i & 0xff) == 0 || i + 1 == groups.Count)
                    progress?.Report(new OperationProgress(phase, i + 1, groups.Count));
            }
            return result;
        }, cancellationToken);

    private static BranchProjection BuildBranch(
        FileSystemPath branch,
        IReadOnlyList<BranchFilePlacement> placements,
        CancellationToken cancellationToken)
    {
        var root = new BranchProjectionNode(branch.Value, branch);
        foreach (var placement in placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = root;
            foreach (var directory in placement.Directories)
            {
                var child = current.Children.FirstOrDefault(node =>
                    node.Directory is not null
                    && StringComparer.OrdinalIgnoreCase.Equals(node.Directory.Value.Value, directory.Value));
                if (child is null)
                {
                    child = new BranchProjectionNode(Path.GetFileName(directory.Value), directory);
                    current.Children.Add(child);
                }
                current = child;
            }

            current.Children.Add(new BranchProjectionNode(
                Path.GetFileName(placement.File.Path.Value),
                file: placement.File));
        }

        PopulateFiles(root, cancellationToken);
        return new BranchProjection(branch, root);
    }

    private static IReadOnlyList<FileInstance> PopulateFiles(
        BranchProjectionNode node,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (node.Children.Count == 0) return node.Files;
        var files = new List<FileInstance>();
        foreach (var child in node.Children)
            files.AddRange(PopulateFiles(child, cancellationToken));
        node.Files = files;
        return files;
    }
}
