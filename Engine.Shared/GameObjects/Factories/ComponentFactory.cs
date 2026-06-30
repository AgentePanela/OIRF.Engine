using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Engine.Shared.IoC;

namespace Engine.Shared.GameObjects.Factories;

[RegisterIoC]
public sealed class ComponentFactory
{
    [Dependency] private readonly EntityManager _entMan = default!;
    [Dependency] private readonly SharedContentManager _contentMan = default!;

    public Dictionary<string, Type> Components {get; private set;} = new();
    public Dictionary<string, Type> ComponentsSanitazed {get; private set;} = new();

    public ComponentFactory()
        => IoCManager.ResolveDependencies(this);

    internal void LoadComponents()
    {
        Log.Debug("Registring components...");
        var types = _contentMan.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            });

        foreach (var type in types)
        {
            if (!type.IsSubclassOf(typeof(Component)) || type.IsAbstract)
                continue;
            
            var attr = type.GetCustomAttribute<RegisterComponentAttribute>();
            if (attr is null)
                throw new Exception($"{type.FullName} inherits Component but is missing [RegisterComponent()].");

            Components.Add(type.Name, type);
            ComponentsSanitazed.Add(attr.Name, type);
        }
    }

    public Type? GetTypeByString(string str)
    {
        if (!Components.ContainsKey(str))
            return null;
        
        return Components[str];
    }

    public string? GetSanitazedByType(Type type)
    {
        foreach (var kvp in ComponentsSanitazed)
        {
            if (kvp.Value == type)
                return kvp.Key;
        }

        return null;
    }

    public string? GetSanitazedByType<T>() where T : Component
        => GetSanitazedByType(typeof(T));

    public Component? CreateInstanceFromSanitazed(string name)
    {
        if (!ComponentsSanitazed.TryGetValue(name, out var type))
            return null;

        return CreateInstance(type);
    }

    public Component? CreateInstance(string name)
    {
        if (!Components.TryGetValue(name, out var type))
            return null;

        return CreateInstance(type);
    }

    public T? CreateInstance<T>() where T : Component
    {
        var type = typeof(T);
        return CreateInstance(type) as T;
    }

    public Component? CreateInstance(Type type)
    {
        if (!Components.ContainsKey(type.Name))
            return null;

        var instance = Activator.CreateInstance(type) as Component;
        if (instance is null)
            return null;
        
        _entMan.CompsPendingAdd.Add(instance);
        return instance;
    }
}
