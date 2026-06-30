using Engine.Shared.Debug.Diagnostics;
using Engine.Shared.IoC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Engine.Shared.GameObjects;

public sealed partial class EntityManager
{
    [Dependency] internal SystemsProfiler _sysProff = default!;
    internal Dictionary<Type, EntitySystem> Systems = new();
    internal readonly Stopwatch _systemTimer = new();

    internal void RegisterSystems()
    {
        Log.Debug("Registrying systems...");
        var types = _contentMan.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            });
        
        foreach (var type in types)
        {
            if (type.IsAbstract || !type.IsSubclassOf(typeof(EntitySystem)))
                continue;
            
            var instance = Activator.CreateInstance(type) as EntitySystem;
            if (instance is null)
                continue;
            
            Log.Debug($"Resgistring system type: {type.Name}");
            Systems.Add(type, instance);
            //IoCManager.ResolveDependencies(instance);
            IoCManager.Register(type, instance);
            instance.SetBus(EventBus);
            
        }

        foreach ((_, var system) in Systems) // resolve dependencies and init the system
        {
            IoCManager.ResolveDependencies(system);
            system.Init();
        }
    }

    internal void OnShutdown()
    {
        foreach ((_, var system) in Systems)
            system.OnShutdown();
    }

    #region System API

    /// <summary>
    /// Get a system using their generic type.
    /// </summary>
    public T? GetSystem<T>() where T : EntitySystem
    {
        var type = typeof(T);
        if (!Systems.TryGetValue(type, out var sys))
            return null;
        return (T) sys;
    }

    /// <summary>
    /// Get a system using their type.
    /// </summary>
    public EntitySystem? GetSystem(Type type)
    {
        if (!Systems.TryGetValue(type, out var sys))
            return null;
        return sys;
    }

    /// <summary>
    /// Get all systems avaible in the registry.
    /// </summary>
    /// <returns></returns>
    public List<EntitySystem> GetAllSystems()
    {
        return Systems.Values.ToList();
    }

    #endregion

    private void UpdateSystems(float dt)
    {
        foreach ((var type, var system) in Systems)
        {
            _systemTimer.Restart();
            system.Update(dt);
            _systemTimer.Stop();
            _sysProff.Record(type.Name, _systemTimer.Elapsed.TotalMilliseconds, 0.0);
        }
    }
}
