namespace Engine.Shared.Threading;

/// <summary>
/// A single unit of work that can be run on the thread pool via <see cref="IParallelManager"/>.
/// Implement as a struct to avoid a heap allocation per dispatch.
/// </summary>
public interface IJob
{
    void Execute();
}
