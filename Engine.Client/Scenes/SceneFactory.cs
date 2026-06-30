using Engine.Shared.IoC;
using Engine.Client.Scenes;
using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Shared;

namespace Engine.Client.Scenes.Factories;

[RegisterIoC]
public sealed class SceneFactory
{
    [Dependency] private readonly SharedContentManager _contentMan = default!;
    public Dictionary<string, Type> Scenes {get; private set;} = new();

    public SceneFactory()
        => IoCManager.ResolveDependencies(this);

    internal void LoadScenes()
    {
        Log.Debug("Registring scenes...");
        var types = _contentMan.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            });

        foreach (var type in types)
        {
            if (!type.IsSubclassOf(typeof(Scene)) || type.IsAbstract)
                continue;

            Scenes.Add(type.Name, type);
            //Log.Debug($"Registring scene {type.Name} from {type.FullName}");
        }
        //Scenes.Add("Scene", Scene.GetType());
    }

    public Type? GetTypeByString(string str)
    {
        if (!Scenes.ContainsKey(str))
            return null;
        
        return Scenes[str];
    }

    public Scene? CreateInstance(string name)
    {
        if (!Scenes.TryGetValue(name, out var type))
            return null;

        return Activator.CreateInstance(type) as Scene;
    }

    public Scene? CreateInstance<T>() where T : Scene
    {
        var type = typeof(T);
        if (!Scenes.ContainsKey(type.Name))
            return null;

        var instance = Activator.CreateInstance(type);
        if (instance is not null && instance is Scene scene)
            return scene;
        
        throw new Exception("Something went wrong!");
    }
}
