using System.Threading;

namespace Engine.Shared.Threading;

/// <summary>
/// Dispatches <see cref="IJob"/>/<see cref="IParallelRangeJob"/> work onto the thread pool.
/// Small workloads (below a job's <see cref="IParallelRangeJob.MinimumBatchParallel"/>) run
/// inline instead of paying dispatch overhead.
/// </summary>
public interface IParallelManager
{
    /// <summary>
    /// How many batches can run concurrently. Backed by the <c>threading.parallelprocesscount</c> CVar.
    /// </summary>
    int ParallelProcessCount { get; }

    /// <summary>
    /// Runs a single job on the thread pool and blocks until it's done.
    /// </summary>
    void ProcessNow(IJob job);

    /// <summary>
    /// Queues a single job on the thread pool without blocking.
    /// </summary>
    WaitHandle Process(IJob job);

    /// <summary>
    /// Splits <paramref name="amount"/> indices into batches and runs them across the thread
    /// pool, blocking until all batches finish.
    /// </summary>
    void ProcessNow(IParallelRangeJob job, int amount);

    /// <summary>
    /// Runs <paramref name="job"/> over <paramref name="amount"/> indices on the calling
    /// thread, ignoring parallelism entirely.
    /// </summary>
    void ProcessSerialNow(IParallelRangeJob job, int amount);

    /// <summary>
    /// Splits <paramref name="amount"/> indices into batches and queues them on the thread
    /// pool without blocking.
    /// </summary>
    WaitHandle Process(IParallelRangeJob job, int amount);
}
