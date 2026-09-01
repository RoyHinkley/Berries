using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using Berries.Core;
using Berries.Core.Domain;
using Berries.Projection;

namespace Berries.Gui;

public partial class MainWindow
{
    private const int GroupExplorerBatchSize = 32;

    private CancellationTokenSource? navigationCancellation;
    private long navigationGeneration;
    private GroupsExplorerCache? groupsExplorerCache;

    private bool NavigationIsActive => navigationCancellation is not null;

    private NavigationOperation BeginNavigation(string text, bool indeterminate = true)
    {
        navigationCancellation?.Cancel();

        var source = new CancellationTokenSource();
        navigationCancellation = source;
        var operation = new NavigationOperation(++navigationGeneration, source, text);
        BeginProgress(text, indeterminate);
        operation.Mark("begin");
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
        operation.Mark("complete");
        if (IsCurrentNavigation(operation))
        {
            EndProgress(text);
            navigationCancellation = null;
        }
        operation.Source.Dispose();
    }

    private void RetireNavigation(NavigationOperation operation)
    {
        operation.Mark("retired");
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
        var buildTime = TimeSpan.Zero;
        var publishTime = TimeSpan.Zero;
        var yieldTime = TimeSpan.Zero;
        operation.Mark($"Groups tree start ({completed:N0}/{total:N0})");
        ShowNavigationProgress(operation, new OperationProgress("Building Groups tree", completed, total));

        while (completed < total)
        {
            token.ThrowIfCancellationRequested();
            var first = completed;
            var count = Math.Min(GroupExplorerBatchSize, total - first);

            var phase = Stopwatch.StartNew();
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
            buildTime += phase.Elapsed;

            if (!IsCurrentNavigation(operation))
                throw new OperationCanceledException(token);

            phase.Restart();
            foreach (var node in batch)
                nodes.Add(node);

            completed += batch.Length;
            builtThrough?.Invoke(completed);
            ShowNavigationProgress(operation, new OperationProgress("Building Groups tree", completed, total));
            publishTime += phase.Elapsed;

            // Yield at input priority: pending input can run, but continued tree construction
            // does not sit behind every background/layout/render consequence of the prior batch.
            phase.Restart();
            await Dispatcher.Yield(DispatcherPriority.Input);
            yieldTime += phase.Elapsed;
        }

        operation.Mark(
            $"Groups tree complete ({completed:N0}/{total:N0}); "
            + $"build {buildTime.TotalMilliseconds:N1} ms, "
            + $"publish {publishTime.TotalMilliseconds:N1} ms, "
            + $"dispatcher wait {yieldTime.TotalMilliseconds:N1} ms");
    }

    private sealed class GroupsExplorerCache(Portrait portrait)
    {
        public Portrait Portrait { get; } = portrait;
        public ObservableCollection<ExplorerNode> Nodes { get; } = [];
        public int BuiltCount { get; set; }
    }

    private sealed class NavigationOperation(
        long generation,
        CancellationTokenSource source,
        string description)
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private TimeSpan previous;

        public long Generation { get; } = generation;
        public CancellationTokenSource Source { get; } = source;
        public CancellationToken Token => Source.Token;

        public void Mark(string phase)
        {
            var elapsed = stopwatch.Elapsed;
            var delta = elapsed - previous;
            previous = elapsed;
            Debug.WriteLine(
                $"[Navigation {Generation}] {description}: {phase} | +{delta.TotalMilliseconds:N1} ms | {elapsed.TotalMilliseconds:N1} ms total");
        }

        public void MarkWhenUiSettled(string phase)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    var elapsed = stopwatch.Elapsed;
                    Debug.WriteLine(
                        $"[Navigation {Generation}] {description}: {phase} | {elapsed.TotalMilliseconds:N1} ms total (deferred UI marker)");
                },
                DispatcherPriority.Background);
        }
    }
}
