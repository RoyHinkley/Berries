using System.Diagnostics;
using Berries.Core.Analysis;
using Berries.Core.Domain;
using Berries.FileSystem.Abstractions;

namespace Berries.Core;

/// <summary>
/// Coordinates acquisition, session construction, portrait mutation, and derived analysis
/// without presentation concerns.
/// </summary>
public sealed class BerriesApplication
{
    private readonly IFileSystem fileSystem;
    private readonly BerriesEngine engine;
    private readonly BranchStatisticsAnalyzer branchStatisticsAnalyzer;
    private readonly BranchCounterpartAnalyzer branchCounterpartAnalyzer;
    private readonly SemaphoreSlim portraitMutation = new(1, 1);
    private readonly object schedulerGate = new();
    private readonly AnalysisProduct<DirectoryAnalysisResult> directoryAnalysis = new();
    private readonly AnalysisProduct<BranchStatisticsResult> branchStatistics = new();
    private readonly AnalysisProduct<BranchPairSuggestionResult> suggestions = new();
    private CancellationTokenSource sessionCancellation = new();
    private Task schedulerTask = Task.CompletedTask;
    private bool schedulerRequested;
    private IProgress<OperationProgress>? scheduledProgress;
    private long portraitGeneration;

    public BerriesApplication(
        IFileSystem fileSystem,
        BerriesEngine engine,
        BranchStatisticsAnalyzer branchStatisticsAnalyzer,
        BranchCounterpartAnalyzer branchCounterpartAnalyzer)
    {
        this.fileSystem = fileSystem;
        this.engine = engine;
        this.branchStatisticsAnalyzer = branchStatisticsAnalyzer;
        this.branchCounterpartAnalyzer = branchCounterpartAnalyzer;
    }

    public event Action<OperationProgress>? AnalysisProgressChanged;
    public event Action? AnalysisChanged;

    public Corpus? Corpus { get; private set; }
    public BerriesSession? Session { get; private set; }
    public ScanResult? Scan { get; private set; }
    public long PortraitGeneration => Interlocked.Read(ref portraitGeneration);
    public DirectoryAnalysisResult? DirectoryAnalysis =>
        directoryAnalysis.IsValid(PortraitGeneration) ? directoryAnalysis.Result : null;
    public BranchStatisticsResult? BranchStatistics =>
        branchStatistics.IsValid(PortraitGeneration) ? branchStatistics.Result : null;
    public BranchPairSuggestionResult? Suggestions =>
        suggestions.IsValid(PortraitGeneration) ? suggestions.Result : null;

    public IReadOnlyList<string> NormalizeRoots(IEnumerable<string> rootPaths) =>
        engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path)))
            .Roots
            .Select(root => root.Path.Value)
            .ToArray();

    public async Task<ScanResult> ScanAsync(
        IEnumerable<string> rootPaths,
        Func<FileSystemPath, bool>? excludePath = null,
        IProgress<ScanProgress>? scanProgress = null,
        IProgress<GroupDiscoveryProgress>? groupProgress = null,
        IProgress<OperationProgress>? analysisProgress = null,
        CancellationToken cancellationToken = default)
    {
        await portraitMutation.WaitAsync(cancellationToken);
        try
        {
            ClearSessionState();

            var totalTimer = Stopwatch.StartNew();
            var phaseTimer = Stopwatch.StartNew();
            Debug.WriteLine("[Berries] Normalizing corpus roots...");
            Corpus = engine.CreateCorpus(rootPaths.Select(path => new FileSystemPath(path)));
            phaseTimer.Stop();
            var normalizationElapsed = phaseTimer.Elapsed;

            Debug.WriteLine("[Berries] Acquiring initial portrait...");
            phaseTimer.Restart();
            var acquired = await engine.BuildInitialPortraitAsync(
                Corpus,
                excludePath,
                scanProgress,
                cancellationToken);
            phaseTimer.Stop();
            var portraitElapsed = phaseTimer.Elapsed;

            Debug.WriteLine("[Berries] Discovering Groups and preparing session portrait...");
            var discovery = await engine.DiscoverGroupsAsync(acquired, groupProgress, cancellationToken);
            Session = new BerriesSession(
                fileSystem,
                discovery.Portrait,
                discovery.UniqueFileCountsByDirectory);

            totalTimer.Stop();
            Scan = new ScanResult(
                Corpus.Roots.Select(root => root.Path.Value).ToArray(),
                discovery.FileCount,
                discovery.TotalBytes,
                discovery.Groups.Count,
                discovery.GroupedFileCount,
                normalizationElapsed,
                portraitElapsed,
                discovery.Timing.Total,
                totalTimer.Elapsed,
                discovery.Evictions.Count);

            AdvancePortraitGeneration();
            Debug.WriteLine("[Berries] Primary scan ready; derived analysis scheduled in background.");
        }
        finally
        {
            portraitMutation.Release();
        }

        ScheduleAnalysis(analysisProgress);
        return Scan!;
    }

    public Task ExcludeAsync(IReadOnlyList<FileInstance> files, CancellationToken cancellationToken = default) =>
        RunSessionCommandAsync(session => session.Exclude(files), cancellationToken);

    public Task DeleteAsync(IReadOnlyList<FileInstance> files, CancellationToken cancellationToken = default) =>
        RunSessionCommandAsync(session => session.Delete(files), cancellationToken);

    public Task<MoveResult> MoveAsync(
        IReadOnlyList<FileInstance> files,
        FileSystemPath source,
        FileSystemPath destination,
        CancellationToken cancellationToken = default) =>
        RunSessionCommandAsync(session => session.Move(files, source, destination), cancellationToken);

    public Task<bool> UndoAsync(CancellationToken cancellationToken = default) =>
        RunSessionCommandAsync(session => session.Undo(), cancellationToken);

    /// <summary>
    /// Ensures all currently defined derived products are valid for the current portrait generation.
    /// Analysis is normally scheduled automatically; this method is primarily an awaitable synchronization point.
    /// </summary>
    public async Task RefreshAnalysisAsync(CancellationToken cancellationToken = default) =>
        await RefreshAnalysisAsync(null, cancellationToken);

    public async Task RefreshAnalysisAsync(
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Corpus is null || Session is null)
                throw new InvalidOperationException("A session must exist before analysis.");

            ScheduleAnalysis(progress);
            Task running;
            lock (schedulerGate) running = schedulerTask;
            await running.WaitAsync(cancellationToken);

            var generation = PortraitGeneration;
            if (directoryAnalysis.IsValid(generation)
                && branchStatistics.IsValid(generation)
                && suggestions.IsValid(generation))
                return;
        }
    }

    public async Task<BestBranchPairResult?> FindBestBranchPairAsync(
        FileSystemPath branch,
        CancellationToken cancellationToken = default)
    {
        if (Corpus is null || Session is null)
            return null;

        if (BranchStatistics is null)
            await RefreshAnalysisAsync(cancellationToken);

        var branches = BranchStatistics;
        if (branches is null || Corpus is null || Session is null)
            return null;

        return await Task.Run(() => branchCounterpartAnalyzer.FindBestPair(
            Corpus,
            branch,
            branches.Branches,
            Session.Groups,
            cancellationToken,
            ForwardProgress(null)), cancellationToken);
    }

    private async Task RunSessionCommandAsync(Action<BerriesSession> command, CancellationToken cancellationToken)
    {
        await portraitMutation.WaitAsync(cancellationToken);
        try
        {
            var session = Session ?? throw new InvalidOperationException("A session must exist before a portrait operation.");
            var operationCount = session.Operations.Count;
            await Task.Run(() => command(session), cancellationToken);
            if (session.Operations.Count != operationCount)
                AdvancePortraitGeneration();
        }
        finally
        {
            portraitMutation.Release();
        }

        ScheduleAnalysis();
    }

    private async Task<T> RunSessionCommandAsync<T>(Func<BerriesSession, T> command, CancellationToken cancellationToken)
    {
        T result;
        await portraitMutation.WaitAsync(cancellationToken);
        try
        {
            var session = Session ?? throw new InvalidOperationException("A session must exist before a portrait operation.");
            var operationCount = session.Operations.Count;
            result = await Task.Run(() => command(session), cancellationToken);
            if (session.Operations.Count != operationCount)
                AdvancePortraitGeneration();
        }
        finally
        {
            portraitMutation.Release();
        }

        ScheduleAnalysis();
        return result;
    }

    private void ScheduleAnalysis(IProgress<OperationProgress>? progress = null)
    {
        lock (schedulerGate)
        {
            schedulerRequested = true;
            if (progress is not null)
                scheduledProgress = progress;
            if (schedulerTask.IsCompleted)
                StartSchedulerLocked();
        }
    }

    private void StartSchedulerLocked()
    {
        schedulerRequested = false;
        var progress = scheduledProgress;
        scheduledProgress = null;
        var sessionToken = sessionCancellation.Token;

        schedulerTask = Task.Run(async () =>
        {
            try
            {
                await RunAnalysisSchedulerAsync(progress, sessionToken);
            }
            finally
            {
                lock (schedulerGate)
                {
                    if (schedulerRequested && !sessionCancellation.IsCancellationRequested)
                        StartSchedulerLocked();
                }
            }
        });
    }

    private async Task RunAnalysisSchedulerAsync(
        IProgress<OperationProgress>? progress,
        CancellationToken sessionToken)
    {
        while (!sessionToken.IsCancellationRequested)
        {
            var generation = PortraitGeneration;
            try
            {
                var snapshot = await CaptureSnapshotAsync(generation, sessionToken);
                if (snapshot is null)
                {
                    if (Corpus is null || Session is null)
                        return;
                    continue;
                }

                if (!directoryAnalysis.IsValid(generation))
                {
                    if (!directoryAnalysis.TryBegin(generation, sessionToken, out var token))
                        continue;
                    try
                    {
                        Debug.WriteLine($"[Berries] Analyzing directories for portrait {generation}...");
                        var result = await engine.AnalyzeDirectoriesAsync(
                            snapshot.Portrait,
                            snapshot.Groups,
                            snapshot.UniqueFileCountsByDirectory,
                            ForwardProgress(progress),
                            token);
                        if (directoryAnalysis.TryPublish(generation, PortraitGeneration, result))
                            AnalysisChanged?.Invoke();
                    }
                    catch (OperationCanceledException) when (!sessionToken.IsCancellationRequested) { }
                    finally { directoryAnalysis.EndRun(generation); }
                    continue;
                }

                var directories = directoryAnalysis.Result!;
                if (!branchStatistics.IsValid(generation))
                {
                    if (!branchStatistics.TryBegin(generation, sessionToken, out var token))
                        continue;
                    try
                    {
                        Debug.WriteLine($"[Berries] Analyzing branch statistics for portrait {generation}...");
                        var result = await Task.Run(() => branchStatisticsAnalyzer.Analyze(
                            snapshot.Corpus,
                            snapshot.Portrait,
                            snapshot.Groups,
                            directories.Directories,
                            snapshot.UniqueFileCountsByDirectory,
                            token,
                            ForwardProgress(progress)), token);
                        if (branchStatistics.TryPublish(generation, PortraitGeneration, result))
                            AnalysisChanged?.Invoke();
                    }
                    catch (OperationCanceledException) when (!sessionToken.IsCancellationRequested) { }
                    finally { branchStatistics.EndRun(generation); }
                    continue;
                }

                var branches = branchStatistics.Result!;
                if (!suggestions.IsValid(generation))
                {
                    if (!suggestions.TryBegin(generation, sessionToken, out var token))
                        continue;
                    try
                    {
                        Debug.WriteLine($"[Berries] Finding Suggestions for portrait {generation}...");
                        var result = await Task.Run(() => branchCounterpartAnalyzer.Analyze(
                            snapshot.Corpus,
                            branches.Branches,
                            snapshot.Groups,
                            directories.DirectoryPairs,
                            suggestionLimit: 25,
                            counterpartLimit: 5,
                            cancellationToken: token,
                            progress: ForwardProgress(progress)), token);
                        if (suggestions.TryPublish(generation, PortraitGeneration, result))
                        {
                            Debug.WriteLine($"[Berries] Analysis ready: {snapshot.Groups.Count:N0} Groups, {result.Suggestions.Count:N0} Suggestions.");
                            AnalysisChanged?.Invoke();
                        }
                    }
                    catch (OperationCanceledException) when (!sessionToken.IsCancellationRequested) { }
                    finally { suggestions.EndRun(generation); }
                    continue;
                }

                if (generation == PortraitGeneration)
                    return;
            }
            catch (OperationCanceledException) when (!sessionToken.IsCancellationRequested)
            {
                // A portrait change made this generation obsolete. Re-evaluate dependencies.
            }
        }
    }

    private async Task<AnalysisSnapshot?> CaptureSnapshotAsync(long expectedGeneration, CancellationToken cancellationToken)
    {
        await portraitMutation.WaitAsync(cancellationToken);
        try
        {
            if (expectedGeneration != PortraitGeneration || Corpus is null || Session is null)
                return null;
            return new AnalysisSnapshot(
                Corpus,
                Session.WorkingPortrait,
                Session.Groups,
                Session.UniqueFileCountsByDirectory);
        }
        finally
        {
            portraitMutation.Release();
        }
    }

    private void AdvancePortraitGeneration()
    {
        var generation = Interlocked.Increment(ref portraitGeneration);
        directoryAnalysis.CancelObsolete(generation);
        branchStatistics.CancelObsolete(generation);
        suggestions.CancelObsolete(generation);
        AnalysisChanged?.Invoke();
    }

    private void ClearSessionState()
    {
        sessionCancellation.Cancel();
        sessionCancellation.Dispose();
        sessionCancellation = new CancellationTokenSource();
        Interlocked.Increment(ref portraitGeneration);
        directoryAnalysis.Reset();
        branchStatistics.Reset();
        suggestions.Reset();
        Corpus = null;
        Session = null;
        Scan = null;
        AnalysisChanged?.Invoke();
    }

    private IProgress<OperationProgress> ForwardProgress(IProgress<OperationProgress>? progress) =>
        new CallbackProgress<OperationProgress>(value =>
        {
            progress?.Report(value);
            AnalysisProgressChanged?.Invoke(value);
        });

    private sealed record AnalysisSnapshot(
        Corpus Corpus,
        Portrait Portrait,
        IReadOnlyList<Group> Groups,
        IReadOnlyDictionary<FileSystemPath, int> UniqueFileCountsByDirectory);

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}

public sealed record ScanResult(
    IReadOnlyList<string> Roots,
    int FileCount,
    long TotalBytes,
    int GroupCount,
    int GroupedFileCount,
    TimeSpan CorpusNormalizationElapsed,
    TimeSpan PortraitAcquisitionElapsed,
    TimeSpan GroupDiscoveryElapsed,
    TimeSpan TotalElapsed,
    int EvictionCount);
