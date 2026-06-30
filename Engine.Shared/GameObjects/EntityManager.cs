using System;
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

        if (EntitiesToRemove.Count > 0)
        {
            var snapshot = _tempUids;
            snapshot.AddRange(EntitiesToRemove);
            //EntitiesToRemove.Clear();

            foreach (var uid in snapshot)
            {
                var entComps = GetEntityComps(uid);
                if (entComps is null)
                    continue;
                
                foreach (var comp in entComps)
                    comp.RemoveComponent(); // mark entity components to be removed (this will happen in the next loop lol)
            }
            snapshot.Clear();
        }

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
                _scene.Entities.Remove(uid);
            }
            snapshot.Clear();
        }
    }

    internal void Draw(float dt)
    {
        DrawSystems(dt);
    }

    private Dictionary<EntityUid, Component> GetPool(Type type)
    {
        if (!_scene.Components.TryGetValue(type, out var pool))
        {
            pool = new Dictionary<EntityUid, Component>();
            _scene.Components[type] = pool;
        }

        return pool;
    }

    private Dictionary<EntityUid, Component> GetPool<T>() where T : Component
    {
        return GetPool(typeof(T));
    }
    
}
