using System;
using System.Linq;
using Engine.Shared.IoC;
using Engine.Shared.Threading;

namespace Engine.Shared.GameObjects;

public sealed partial class EntityManager
{
    [Dependency] private readonly IParallelManager _parallelMan = default!;

    #region Parallel Query Api

    /// <summary>
    /// Runs <paramref name="job"/> once per entity with <typeparamref name="T"/>, across
    /// multiple CPU cores. Only worth it for count-heavy or per-entity-expensive work - plain
    /// <see cref="Query{T}"/> is faster for cheap per-entity work. See the ECS docs' "Parallel
    /// Queries" section for the full safety contract: entity/component mutation and EventBus
    /// calls are unsafe from inside <paramref name="job"/> (throws in Debug via
    /// <see cref="MainThread.AssertMainThread"/>, silently unsafe in Release), as is anything
    /// GPU-related (unenforced).
    /// </summary>
    public void ParallelQuery<T, TJob>(TJob job)
        where T : Component
        where TJob : struct, IEntityJob<T>
    {
        var snapshot = Query<T>().ToArray();
        if (snapshot.Length == 0)
            return;

        _parallelMan.ProcessNow(new EntityRangeJob<T, TJob>(snapshot, job), snapshot.Length);
    }

    /// <inheritdoc cref="ParallelQuery{T, TJob}(TJob)"/>
    /// <remarks>Convenience overload - allocates a closure if <paramref name="body"/> captures locals.</remarks>
    public void ParallelQuery<T>(Action<EntityUid, T> body) where T : Component
        => ParallelQuery<T, DelegateEntityJob<T>>(new DelegateEntityJob<T>(body));

    /// <inheritdoc cref="ParallelQuery{T, TJob}(TJob)"/>
    /// <remarks>The entity-matching join (smaller-pool-first) runs sequentially before parallel work begins.</remarks>
    public void ParallelQuery<T1, T2, TJob>(TJob job)
        where T1 : Component where T2 : Component
        where TJob : struct, IEntityJob<T1, T2>
    {
        var snapshot = Query<T1, T2>().ToArray();
        if (snapshot.Length == 0)
            return;

        _parallelMan.ProcessNow(new EntityRangeJob<T1, T2, TJob>(snapshot, job), snapshot.Length);
    }

    /// <inheritdoc cref="ParallelQuery{T1, T2, TJob}(TJob)"/>
    public void ParallelQuery<T1, T2>(Action<EntityUid, T1, T2> body) where T1 : Component where T2 : Component
        => ParallelQuery<T1, T2, DelegateEntityJob<T1, T2>>(new DelegateEntityJob<T1, T2>(body));

    /// <inheritdoc cref="ParallelQuery{T1, T2, TJob}(TJob)"/>
    /// <remarks>The entity-matching join (smallest-pool-first) runs sequentially before parallel work begins.</remarks>
    public void ParallelQuery<T1, T2, T3, TJob>(TJob job)
        where T1 : Component where T2 : Component where T3 : Component
        where TJob : struct, IEntityJob<T1, T2, T3>
    {
        var snapshot = Query<T1, T2, T3>().ToArray();
        if (snapshot.Length == 0)
            return;

        _parallelMan.ProcessNow(new EntityRangeJob<T1, T2, T3, TJob>(snapshot, job), snapshot.Length);
    }

    /// <inheritdoc cref="ParallelQuery{T1, T2, T3, TJob}(TJob)"/>
    public void ParallelQuery<T1, T2, T3>(Action<EntityUid, T1, T2, T3> body)
        where T1 : Component where T2 : Component where T3 : Component
        => ParallelQuery<T1, T2, T3, DelegateEntityJob<T1, T2, T3>>(new DelegateEntityJob<T1, T2, T3>(body));

    private readonly struct EntityRangeJob<T, TJob> : IParallelJob
        where T : Component where TJob : struct, IEntityJob<T>
    {
        private readonly (EntityUid uid, T comp)[] _entities;
        private readonly TJob _job;

        public EntityRangeJob((EntityUid uid, T comp)[] entities, TJob job)
        {
            _entities = entities;
            _job = job;
        }

        public void Execute(int index)
        {
            var (uid, comp) = _entities[index];
            _job.Execute(uid, comp);
        }
    }

    private readonly struct EntityRangeJob<T1, T2, TJob> : IParallelJob
        where T1 : Component where T2 : Component where TJob : struct, IEntityJob<T1, T2>
    {
        private readonly (EntityUid uid, T1 comp1, T2 comp2)[] _entities;
        private readonly TJob _job;

        public EntityRangeJob((EntityUid uid, T1 comp1, T2 comp2)[] entities, TJob job)
        {
            _entities = entities;
            _job = job;
        }

        public void Execute(int index)
        {
            var (uid, comp1, comp2) = _entities[index];
            _job.Execute(uid, comp1, comp2);
        }
    }

    private readonly struct EntityRangeJob<T1, T2, T3, TJob> : IParallelJob
        where T1 : Component where T2 : Component where T3 : Component where TJob : struct, IEntityJob<T1, T2, T3>
    {
        private readonly (EntityUid uid, T1, T2, T3)[] _entities;
        private readonly TJob _job;

        public EntityRangeJob((EntityUid uid, T1, T2, T3)[] entities, TJob job)
        {
            _entities = entities;
            _job = job;
        }

        public void Execute(int index)
        {
            var (uid, comp1, comp2, comp3) = _entities[index];
            _job.Execute(uid, comp1, comp2, comp3);
        }
    }

    private readonly struct DelegateEntityJob<T> : IEntityJob<T> where T : Component
    {
        private readonly Action<EntityUid, T> _body;
        public DelegateEntityJob(Action<EntityUid, T> body) => _body = body;
        public void Execute(EntityUid uid, T comp) => _body(uid, comp);
    }

    private readonly struct DelegateEntityJob<T1, T2> : IEntityJob<T1, T2> where T1 : Component where T2 : Component
    {
        private readonly Action<EntityUid, T1, T2> _body;
        public DelegateEntityJob(Action<EntityUid, T1, T2> body) => _body = body;
        public void Execute(EntityUid uid, T1 comp1, T2 comp2) => _body(uid, comp1, comp2);
    }

    private readonly struct DelegateEntityJob<T1, T2, T3> : IEntityJob<T1, T2, T3>
        where T1 : Component where T2 : Component where T3 : Component
    {
        private readonly Action<EntityUid, T1, T2, T3> _body;
        public DelegateEntityJob(Action<EntityUid, T1, T2, T3> body) => _body = body;
        public void Execute(EntityUid uid, T1 comp1, T2 comp2, T3 comp3) => _body(uid, comp1, comp2, comp3);
    }

    #endregion
}
