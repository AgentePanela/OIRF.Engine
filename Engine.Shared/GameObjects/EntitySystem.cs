using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Engine.Shared.Prototypes;
using Microsoft.Xna.Framework;
using static Engine.Shared.GameObjects.EventBus;

namespace Engine.Shared.GameObjects;

/// <summary>
/// Main class to every component system.
/// </summary>
public abstract class EntitySystem
{
    [Dependency] protected readonly EntityManager _entManager;

    /// <summary>
    /// Stops the update calls in this system.
    /// </summary>
    public bool FreezeUpdate = false;
    protected EventBus _bus;

    // just for entity registration
    internal void SetBus(EventBus bus)
        => _bus = bus;

    /// <summary>
    /// Called once when the system is registered.
    /// Use this to initialize resources and subscribe to events.
    /// </summary>
    public virtual void Init()
    {
    }

    /// <summary>
    /// Called every frame to update the system logic.
    /// </summary>
    /// <param name="dt">Delta time in seconds since the last frame.</param>
    public virtual void Update(float dt)
    {
    }

    /// <summary>
    /// Called right before the main process shutdown.
    /// </summary>
    public virtual void OnShutdown()
    {
        
    }

    /// <summary>
    /// Get the transform component from an entity (if it has one).
    /// </summary>
    protected TransformComponent? Transform(EntityUid uid)
    {
        if (!TryComp<TransformComponent>(uid, out var transform))
            return null;

        return transform;
    }

    /// <summary>
    /// Return true if a entity has trasnform and get the transform component from the entity.
    /// </summary>
    protected bool TryTransform(EntityUid uid, [NotNullWhen(true)]out TransformComponent? transform)
    {
        transform = Transform(uid);
        if (transform is null)
            return false;
        return true;
    }
    
    /// ================
    /// Event Bus Wrappers
    /// ================
    
    /// <inheritdoc cref="EventBus.Subscribe{T}(GlobalEventHandler{T})"/>
    public void SubscribeEvent<T>(GlobalEventHandler<T> handler) where T : EntityEvent
        => _bus.Subscribe<T>(handler);
    
    /// <inheritdoc cref="EventBus.Subscribe{CompT, EventT}(EntityEventHandler{CompT, EventT})"/>
    public void SubscribeEvent<CompT, EventT>(EntityEventHandler<CompT, EventT> handler)
        where CompT : Component where EventT : EntityEvent
        => _bus.Subscribe<CompT, EventT>(handler);
    
    /// <inheritdoc cref="EventBus.RaiseEvent{T}(T)"/>
    public void RaiseEvent<T>(T ev) where T : EntityEvent
        => _bus.RaiseEvent<T>(ev);

    /// <inheritdoc cref="EventBus.RaiseEvent{T}(EntityUid, T)"/>
    public void RaiseEvent<T>(EntityUid uid, T ev) where T : EntityEvent
        => _bus.RaiseEvent<T>(uid, ev);

    /// ================
    /// Entity Manager wrappers.
    /// ================

    // ==== Components

    /// <inheritdoc cref="EntityManager.AddComp{T}(EntityUid)"/>
    protected T AddComp<T>(EntityUid uid) where T : Component, new() => _entManager.AddComp<T>(uid);

    /// <inheritdoc cref="EntityManager.EnsureComp{T}(EntityUid)"/>
    protected T EnsureComp<T>(EntityUid uid) where T : Component, new() => _entManager.EnsureComp<T>(uid);

    /// <inheritdoc cref="EntityManager.GetComp{T}(EntityUid)" />
    protected T? GetComp<T>(EntityUid uid) where T : Component => _entManager.GetComp<T>(uid);

    /// <inheritdoc cref="EntityManager.TryComp{T}(EntityUid, out T?)"/>>
    protected bool TryComp<T>(EntityUid uid, [NotNullWhen(true)]out T? comp) where T : Component => _entManager.TryComp<T>(uid, out comp);

    /// <inheritdoc cref="EntityManager.HasComp{T}(EntityUid)"/>
    protected bool HasComp<T>(EntityUid uid) where T : Component => _entManager.HasComp<T>(uid);

    /// <inheritdoc cref="EntityManager.RemComp{T}(EntityUid)"/>
    protected void RemComp<T>(EntityUid uid) where T : Component => _entManager.RemComp<T>(uid);

    /// <inheritdoc cref="EntityManager.GetEntityComps(EntityUid)"/>
    public List<Component>? GetEntityComps(EntityUid uid)
        => _entManager.GetEntityComps(uid);

    /// <inheritdoc cref="EntityManager.Query{T}()"/>
    protected ComponentQuery<T>
        GetEntitiesWithComp<T>() where T : Component
            => _entManager.Query<T>();

    /// <inheritdoc cref="EntityManager.Query{T1, T2}()"/>
    protected ComponentQuery<T1, T2>
        GetEntitiesWithComp<T1, T2>() where T1 : Component where T2 : Component
            => _entManager.Query<T1, T2>();

    /// <inheritdoc cref="EntityManager.Query{T1, T2, T3}()"/>
    protected ComponentQuery<T1, T2, T3>
        GetEntitiesWithComp<T1, T2, T3>() where T1 : Component where T2 : Component where T3 : Component
            => _entManager.Query<T1, T2, T3>();

    /// <inheritdoc cref="EntityManager.ParallelQuery{T, TJob}(TJob)"/>
    protected void ParallelQuery<T, TJob>(TJob job) where T : Component where TJob : struct, IEntityJob<T>
        => _entManager.ParallelQuery<T, TJob>(job);

    /// <inheritdoc cref="EntityManager.ParallelQuery{T}(Action{EntityUid, T})"/>
    protected void ParallelQuery<T>(Action<EntityUid, T> body) where T : Component
        => _entManager.ParallelQuery(body);

    /// <inheritdoc cref="EntityManager.ParallelQuery{T1, T2, TJob}(TJob)"/>
    protected void ParallelQuery<T1, T2, TJob>(TJob job)
        where T1 : Component where T2 : Component where TJob : struct, IEntityJob<T1, T2>
        => _entManager.ParallelQuery<T1, T2, TJob>(job);

    /// <inheritdoc cref="EntityManager.ParallelQuery{T1, T2}(Action{EntityUid, T1, T2})"/>
    protected void ParallelQuery<T1, T2>(Action<EntityUid, T1, T2> body) where T1 : Component where T2 : Component
        => _entManager.ParallelQuery(body);

    /// <inheritdoc cref="EntityManager.ParallelQuery{T1, T2, T3, TJob}(TJob)"/>
    protected void ParallelQuery<T1, T2, T3, TJob>(TJob job)
        where T1 : Component where T2 : Component where T3 : Component where TJob : struct, IEntityJob<T1, T2, T3>
        => _entManager.ParallelQuery<T1, T2, T3, TJob>(job);

    /// <inheritdoc cref="EntityManager.ParallelQuery{T1, T2, T3}(Action{EntityUid, T1, T2, T3})"/>
    protected void ParallelQuery<T1, T2, T3>(Action<EntityUid, T1, T2, T3> body)
        where T1 : Component where T2 : Component where T3 : Component
        => _entManager.ParallelQuery(body);

    // ==== entities
    
    /// <inheritdoc cref="EntityManager.CreateEmptyEntity(string?)"/>
    protected EntityUid CreateEmptyEntity(string? name = default)
        => _entManager.CreateEmptyEntity(name);

    /// <inheritdoc cref="EntityManager.CreateEntity(ProtoId{Prototypes.EntityPrototype}, string?)"/>
    protected EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, string? nameOverride = null)
        => _entManager.CreateEntity(protoId, nameOverride);

    /// <inheritdoc cref="EntityManager.CreateEntity(ProtoId{EntityPrototype}, Microsoft.Xna.Framework.Vector2, string?)"/>
    protected EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, Vector2 pos, string? nameOverride = null)
        => _entManager.CreateEntity(protoId, pos, nameOverride);

    /// <inheritdoc cref="EntityManager.CloneEntity(EntityUid)"/>
    protected EntityUid CloneEntity(EntityUid source)
        => _entManager.CloneEntity(source);
    
    /// <inheritdoc cref="EntityManager.GetEntity(EntityUid)"/>
    protected Entity? GetEntity(EntityUid uid)
        => _entManager.GetEntity(uid);
    
    /// <inheritdoc cref="EntityManager.HasEntity(EntityUid, out Entity?)"/>
    protected bool HasEntity(EntityUid uid, [NotNullWhen(true)] out Entity? ent)
        => _entManager.HasEntity(uid, out ent);

    /// <inheritdoc cref="EntityManager.DeleteEntity(EntityUid)"/>
    protected void DeleteEntity(EntityUid uid)
        => _entManager.DeleteEntity(uid);
}
