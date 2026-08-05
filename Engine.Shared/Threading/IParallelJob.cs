namespace Engine.Shared.Threading;

/// <summary>
/// A <see cref="IParallelRangeJob"/> that processes one index at a time. Implement
/// <see cref="Execute"/> instead of <see cref="IParallelRangeJob.ExecuteRange"/> directly.
/// </summary>
public interface IParallelJob : IParallelRangeJob
{
    void IParallelRangeJob.ExecuteRange(int start, int end)
    {
        for (var i = start; i < end; i++)
            Execute(i);
    }

    void Execute(int index);
}
