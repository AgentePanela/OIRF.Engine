namespace Engine.Shared.Threading;

/// <summary>
/// A job that processes a contiguous range of indices, dispatched in batches across the
/// thread pool by <see cref="IParallelManager"/>. Implement as a struct to avoid a heap
/// allocation per dispatch.
/// </summary>
public interface IParallelRangeJob
{
    /// <summary>
    /// Minimum number of batches required before this job is worth parallelizing.
    /// Below this, <see cref="IParallelManager"/> just calls <see cref="ExecuteRange"/> once, inline.
    /// </summary>
    int MinimumBatchParallel => 2;

    /// <summary>
    /// How many indices each batch covers.
    /// </summary>
    int BatchSize => 1;

    void ExecuteRange(int start, int end);
}
