using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.Prototypes;
using Engine.Shared;
using Engine.Shared.IoC;
using Engine.Shared.GameObjects;

namespace Engine.Shared.GameObjects;

public sealed partial class EntityManager
{
    [Dependency] private ComponentFactory _compFac = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedContentManager _contentMan = default!;
    public EventBus EventBus;
    private IEntityScene _scene;

    internal readonly List<EntityUid> EntitiesToRemove = new();
    internal readonly HashSet<Component> CompsPendingAdd = new();
    internal readonly HashSet<Component> CompsPendingRemove = new();
    private readonly List<Component> _tempComps = new();
    private readonly List<EntityUid> _tempUids = new();
    private bool WipeEntities = false;

    public void Init()
    {
        IoCManager.ResolveDependencies(this);
        EventBus = new EventBus();
        EventBus.Init();
    }

    internal void ForceScene(IEntityScene scene)
    {
        if (_scene == scene)
            return;
        
        _scene = scene;
        Log.Debug("Entity manager current scene updated.");
    }

    internal void Update(float dt)
    {
        UpdateSystems(dt);
        if (_scene is null)
            return;

        if (WipeEntities)
        {
            var ents = GetEntities();
            Log.Debug($"Wiping all {ents.Count} entities...");
            foreach (var ent in ents)
                DeleteEntity(ent);
        }

        var markedForRemoval = new HashSet<EntityUid>();
        while (true)
        {
            var newlyQueued = _tempUids;
            foreach (var uid in EntitiesToRemove)
            {
                if (markedForRemoval.Add(uid))
                    newlyQueued.Add(uid);
            }

            if (newlyQueued.Count == 0 && CompsPendingRemove.Count == 0)
            {
                newlyQueued.Clear();
                break;
            }

            foreach (var uid in newlyQueued)
            {
                var entComps = GetEntityComps(uid);
                if (entComps is null)
                    continue;

                foreach (var comp in entComps)
                    comp.RemoveComponent(); // mark entity components to be removed (processed right below)
            }
            newlyQueued.Clear();

            if (CompsPendingRemove.Count > 0)
            {
                var snapshot = _tempComps;
                snapshot.AddRange(CompsPendingRemove);
                CompsPendingRemove.Clear();

                foreach (var comp in snapshot)
                {
                    EventBus.RaiseEvent(comp.Owner, new CompRemovedEvent() { Component = comp });
                    if (_scene.Components.TryGetValue(comp.GetType(), out var pool))
                        pool.Remove(comp.Owner);
                }
                snapshot.Clear();
            }
        }

        if (CompsPendingAdd.Count > 0)
        {
            var snapshot = _tempComps;
            snapshot.AddRange(CompsPendingAdd);
            CompsPendingAdd.Clear();

            foreach (var comp in snapshot)
            {
                comp.State = Component.CompState.Running;
                EventBus.RaiseEvent(comp.Owner, new CompAddedEvent() { Component = comp });
            }
            snapshot.Clear();
        }

        if (EntitiesToRemove.Count > 0)
        {
            var snapshot = _tempUids;
            snapshot.AddRange(EntitiesToRemove);
            EntitiesToRemove.Clear();

            foreach (var uid in snapshot)
            {
                EventBus.RaiseEvent(uid, new EntityRemovedEvent());
                _scene.Entities.TryRemove(uid, out _);
            }
            snapshot.Clear();
        }

        if (WipeEntities)
        {
            _scene.EntUidIndex = 0;
            WipeEntities = false;
            Log.Debug("All entities has been deleted.");
        }
    }

    private Dictionary<EntityUid, Component> GetPool(Type type)
    {
        return _scene.Components.GetOrAdd(type, static _ => new Dictionary<EntityUid, Component>());
    }

    private Dictionary<EntityUid, Component> GetPool<T>() where T : Component
    {
        return GetPool(typeof(T));
    }
    
}
