using System.Collections.ObjectModel;
using Avalonia.Threading;
using Berries.Core;
using Berries.Core.Domain;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private const int GroupExplorerBatchSize = 64;

    private CancellationTokenSource? navigationCancellation;
    private long navigationGeneration;
    private GroupsExplorerCache? groupsExplorerCache;

    private bool NavigationIsActive => navigationCancellation is not null;

    private NavigationOperation BeginNavigation(string text, bool indeterminate = true)
    {
        navigationCancellation?.Cancel();

        var source = new CancellationTokenSource();
        navigationCancellation = source;
        var operation = new NavigationOperation(++navigationGeneration, source);
        BeginProgress(text, indeterminate);
        return operation;
    }

    private bool IsCurrentNavigation(NavigationOperation operation) =>
        operation.Generation == navigationGeneration
        && ReferenceEquals(navigationCancellation, operation.Source)
        && !operation.Token.IsCancellationRequested;

    private void ShowNavigationProgress(NavigationOperation operation, OperationProgress progress)
    {
        if (IsCurrentNavigation(operation))
            ShowAnalysisProgress(progress);
    }

    private void CompleteNavigation(NavigationOperation operation, string text)
    {
        if (IsCurrentNavigation(operation))
        {
            EndProgress(text);
            navigationCancellation = null;
        }
        operation.Source.Dispose();
    }

    private void RetireNavigation(NavigationOperation operation)
    {
        if (ReferenceEquals(navigationCancellation, operation.Source))
            navigationCancellation = null;
        operation.Source.Dispose();
    }

    private GroupsExplorerCache GetGroupsExplorerCache(Portrait portrait)
    {
        if (groupsExplorerCache is not null && ReferenceEquals(groupsExplorerCache.Portrait, portrait))
            return groupsExplorerCache;

        groupsExplorerCache = new GroupsExplorerCache(portrait);
        return groupsExplorerCache;
    }

    private async Task BuildGroupsExplorerTreeAsync(
        IReadOnlyList<GroupProjection> groups,
        ObservableCollection<ExplorerNode> nodes,
        int startIndex,
        NavigationOperation operation,
        Action<int>? builtThrough = null)
    {
        var token = operation.Token;
        var total = groups.Count;
        var completed = Math.Clamp(startIndex, 0, total);
        ShowNavigationProgress(operation, new OperationProgress("Building Groups tree", completed, total));

        while (completed < total)
        {
            token.ThrowIfCancellationRequested();
            var first = completed;
            var count = Math.Min(GroupExplorerBatchSize, total - first);
            var batch = await Task.Run(() =>
            {
                var result = new ExplorerNode[count];
                for (var i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    result[i] = BuildGroupNode(groups[first + i]);
                }
                return result;
            }, token);

            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(token);

            foreach (var node in batch)
                nodes.Add(node);

            completed += batch.Length;
            builtThrough?.Invoke(completed);
            ShowNavigationProgress(operation, new OperationProgress("Building Groups tree", completed, total));

            // Resume at background priority so pending input, layout, and rendering
            // are serviced before the next batch is published.
            await Dispatcher.UIThread.Yield(DispatcherPriority.Background);
        }
    }

    private sealed class GroupsExplorerCache(Portrait portrait)
    {
        public Portrait Portrait { get; } = portrait;
        public ObservableCollection<ExplorerNode> Nodes { get; } = [];
        public int BuiltCount { get; set; }
    }

    private sealed class NavigationOperation(long generation, CancellationTokenSource source)
    {
        public long Generation { get; } = generation;
        public CancellationTokenSource Source { get; } = source;
        public CancellationToken Token => Source.Token;
    }
}
