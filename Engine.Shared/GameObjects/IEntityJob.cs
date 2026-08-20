namespace Engine.Shared.GameObjects;

/// <inheritdoc cref="IEntityJob{T1, T2}"/>
public interface IEntityJob<T> where T : Component
{
    void Execute(EntityUid uid, T comp);
}

/// <inheritdoc cref="IEntityJob{T1, T2, T3}"/>
public interface IEntityJob<T1, T2> where T1 : Component where T2 : Component
{
    void Execute(EntityUid uid, T1 comp1, T2 comp2);
}

/// <summary>
/// A per-entity unit of work for <see cref="EntityManager.ParallelQuery{T, TJob}"/>.
/// Implement as a struct to avoid a heap allocation per entity.
/// </summary>
public interface IEntityJob<T1, T2, T3> where T1 : Component where T2 : Component where T3 : Component
{
    void Execute(EntityUid uid, T1 comp1, T2 comp2, T3 comp3);
}
