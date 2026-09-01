namespace Berries.Core.Analysis;

/// <summary>
/// Lifecycle state for one derived analysis product. The latest published result is retained
/// even after it becomes stale. Correctness is determined by portrait generation, not by
/// clearing old results.
/// </summary>
public sealed class AnalysisProduct<T> where T : class
{
    private readonly object gate = new();
    private CancellationTokenSource? runningCancellation;

    public T? Result { get; private set; }
    public long ResultGeneration { get; private set; } = -1;
    public long? RunningGeneration { get; private set; }

    public bool IsValid(long currentGeneration)
    {
        lock (gate)
            return Result is not null && ResultGeneration == currentGeneration;
    }

    public bool IsRunning(long generation)
    {
        lock (gate)
            return RunningGeneration == generation;
    }

    public bool TryBegin(long generation, CancellationToken outerCancellation, out CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (Result is not null && ResultGeneration == generation)
            {
                cancellationToken = default;
                return false;
            }

            if (RunningGeneration == generation)
            {
                cancellationToken = default;
                return false;
            }

            runningCancellation?.Cancel();
            runningCancellation?.Dispose();
            runningCancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
            RunningGeneration = generation;
            cancellationToken = runningCancellation.Token;
            return true;
        }
    }

    public bool TryPublishIntermediate(long generation, long currentGeneration, T result)
    {
        lock (gate)
        {
            if (RunningGeneration != generation || generation != currentGeneration)
                return false;

            Result = result;
            ResultGeneration = generation;
            return true;
        }
    }

    public bool TryPublish(long generation, long currentGeneration, T result)
    {
        lock (gate)
        {
            if (RunningGeneration != generation)
                return false;

            FinishRunLocked();
            if (generation != currentGeneration)
                return false;

            Result = result;
            ResultGeneration = generation;
            return true;
        }
    }

    public void EndRun(long generation)
    {
        lock (gate)
        {
            if (RunningGeneration == generation)
                FinishRunLocked();
        }
    }

    public void CancelObsolete(long currentGeneration)
    {
        lock (gate)
        {
            if (RunningGeneration is { } runningGeneration && runningGeneration != currentGeneration)
                runningCancellation?.Cancel();
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            runningCancellation?.Cancel();
            FinishRunLocked();
            Result = null;
            ResultGeneration = -1;
        }
    }

    private void FinishRunLocked()
    {
        runningCancellation?.Dispose();
        runningCancellation = null;
        RunningGeneration = null;
    }
}
