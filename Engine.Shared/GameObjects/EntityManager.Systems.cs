using Engine.Shared.Debug.Diagnostics;
using Engine.Shared.IoC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Engine.Shared.GameObjects;

public sealed partial class EntityManager
{
    [Dependency] internal SystemsProfiler _sysProff = default!;

    /// <summary>
    /// The system type will be ignored during system loading.
    /// </summary>
    public readonly List<Type> SystemBlacklist = new();

    internal Dictionary<Type, EntitySystem> Systems = new();
    internal List<EntitySystem> OrderedSystems = new();

    // lookup used by GetSystem<T>/GetSystem(Type): real type + every abstract ancestor

    private readonly Dictionary<Type, EntitySystem> _systemLookup = new();

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
            if (SystemBlacklist.Contains(type) || type.IsAbstract || !type.IsSubclassOf(typeof(EntitySystem)))
                continue;
            
            var ignore = type.GetCustomAttribute<IgnoreSystemRegistryAttribute>();
            if (ignore is not null)
                continue;

            var instance = Activator.CreateInstance(type) as EntitySystem;
            if (instance is null)
                continue;

            Log.Debug($"Resgistring system type: {type.Name}");
            Systems.Add(type, instance);
            _systemLookup.Add(type, instance);
            //IoCManager.ResolveDependencies(instance);
            IoCManager.Register(type, instance);
            for (var baseType = type.BaseType; baseType is not null && baseType != typeof(EntitySystem) && baseType.IsAbstract; baseType = baseType.BaseType)
            {
                var ignoreBase = baseType.GetCustomAttribute<IgnoreSystemRegistryAttribute>();
                if (ignoreBase is not null)
                    continue;

                _systemLookup.Add(baseType, instance);
                IoCManager.Register(baseType, instance);
            }

            instance.SetBus(EventBus);
        }

        OrderedSystems = Systems.Values
            .OrderBy(s => s.GetType().GetCustomAttribute<SystemPriorityAttribute>()?.Priority ?? 0)
            .ToList();

        foreach (var system in OrderedSystems) // resolve dependencies and init the system
        {
            IoCManager.ResolveDependencies(system);
            system.Init();
        }
    }

    internal void OnShutdown()
    {
        foreach (var system in OrderedSystems)
            system.OnShutdown();
    }

    #region System API

    /// <summary>
    /// Get a system using their generic type.
    /// </summary>
    public T? GetSystem<T>() where T : EntitySystem
    {
        var type = typeof(T);
        if (!_systemLookup.TryGetValue(type, out var sys))
            return null;
        return (T) sys;
    }

    /// <summary>
    /// Get a system using their type.
    /// </summary>
    public EntitySystem? GetSystem(Type type)
    {
        if (!_systemLookup.TryGetValue(type, out var sys))
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
        foreach (var system in OrderedSystems)
        {
            if (system.FreezeUpdate)
                continue;

            _systemTimer.Restart();
            system.Update(dt);
            _systemTimer.Stop();
            _sysProff.Record(system.GetType().Name, _systemTimer.Elapsed.TotalMilliseconds, 0.0);
        }
    }
}
