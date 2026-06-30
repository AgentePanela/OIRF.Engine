using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Engine.Shared.IoC;

/// <summary>
/// Simple IoC container used to register and resolve dependencies.
/// Stores singleton instances and injects dependencies marked with <see cref="DependencyAttribute"/>.
/// </summary>
public static class IoCManager
{
    private static readonly ConcurrentDictionary<Type, object> _singletons = new();

    /// <summary>
    /// Registers a type and creates its instance automatically.
    /// </summary>
    public static void Register<T>() where T : new()
    {
        Register(typeof(T), new T());
    }

    /// <summary>
    /// Registers an already created instance.
    /// </summary>
    public static void Register<T>(T instance)
    {
        Register(typeof(T), instance!);
    }

    /// <summary>
    /// Registers an already created instance and a forced type.
    /// </summary>
    public static void Register(Type type, object instance)
    {
        if (!_singletons.TryAdd(type, instance))
            throw new InvalidOperationException($"Type already registered: {type}");
    }

    /// <summary>
    /// Registers a type using a interface and creates its instance automatically.
    /// </summary>
    public static void Register<TInterface, TImplementation>()  where TImplementation : TInterface, new()
    {
        Register(typeof(TInterface), new TImplementation()!);
    }

    public static void Register<TInterface, TImplementation>(TImplementation instance)  where TImplementation : TInterface, new()
    {
        Register(typeof(TInterface), instance!);
    }

    /// <summary>
    /// Tries to resolve a dependency by type.
    /// Returns false if not registered.
    /// </summary>
    public static bool TryResolve<T>([NotNullWhen(true)]out T? instance)
    {
        var type = typeof(T);
        instance = default;
        if (!TryResolve(type, out var obj))
            return false;

        instance = (T)obj;
        return true;
    }

    /// <summary>
    /// Tries to resolve a dependency by type.
    /// Returns false if not registered.
    /// </summary>
    public static bool TryResolve(Type type, [NotNullWhen(true)]out object? instance)
    {
        if (!_singletons.TryGetValue(type, out instance))
            return false;

        return true;
    }

    /// <summary>
    /// Resolves a dependency. Throws if it is not registered.
    /// </summary>
    public static T Resolve<T>()
    {
        var type = typeof(T);

        return (T)Resolve(type);
    }

    /// <summary>
    /// Resolves a dependency. Throws if it is not registered.
    /// </summary>
    public static object Resolve(Type type)
    {
        if (!_singletons.TryGetValue(type, out var obj))
            throw new Exception($"IoC dependency not registered: {type}");

        return obj;
    }

    public static bool IsRegistered<T>()
        => _singletons.ContainsKey(typeof(T));

    public static bool IsRegistered(Type type)
        => _singletons.ContainsKey(type);

    /// <summary>
    /// Injects dependencies into fields marked with <see cref="DependencyAttribute"/>.
    /// </summary>
    public static void ResolveDependencies(object obj)
    {
        var type = obj.GetType();

        foreach (var field in type.GetFields(
            BindingFlags.Instance |
            BindingFlags.NonPublic |
            BindingFlags.Public))
        {
            if (!Attribute.IsDefined(field, typeof(DependencyAttribute)))
                continue;

            var dep = Resolve(field.FieldType);

            field.SetValue(obj, dep);
        }
    }

    /// <summary>
    /// Automatically registers classes marked with <see cref="RegisterIoCAttribute"/> in selected assembly.
    /// </summary>
    public static void AutoRegister(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<RegisterIoCAttribute>();

            if (attr == null)
                continue;

            if (!attr.Singleton)
                throw new Exception("WIP");
            
            if (attr.RegisterAs is not null)
            {
                var inst = Activator.CreateInstance(type)!;
                _singletons[attr.RegisterAs] = inst;
                continue;
            }

            var instance = Activator.CreateInstance(type)!;
            _singletons[type] = instance;
        }
    }
}
