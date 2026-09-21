using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Engine.Shared.IoC;

namespace Engine.Shared.GameObjects.Factories;

[RegisterIoC]
public sealed class ComponentFactory
{
    [Dependency] private readonly EntityManager _entMan = default!;
    [Dependency] private readonly SharedContentManager _contentMan = default!;

    public Dictionary<string, Type> Components {get; private set;} = new();
    public Dictionary<string, Type> ComponentsSanitized {get; private set;} = new();

    /// <summary>
    /// Components types in here will not be registred during loading.
    /// </summary>
    public readonly List<Type> ComponentsBlacklist = new();

    private readonly List<Type> _networkedTypes = new();
    private readonly Dictionary<Type, int> _networkedIds = new();
    private string _networkedHash = string.Empty;

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
            if (ComponentsBlacklist.Contains(type) || !type.IsSubclassOf(typeof(Component)) || type.IsAbstract)
                continue;
            
            var attr = type.GetCustomAttribute<RegisterComponentAttribute>();
            if (attr is null)
                throw new Exception($"{type.FullName} inherits Component but is missing [RegisterComponent()].");

            Components.Add(type.Name, type);
            ComponentsSanitized.Add(attr.Name, type);
        }

        BuildNetworkedComponents();
    }

    /// <summary>
    /// Numbers the <see cref="NetworkedComponentAttribute"/> components (starting at 1) by their registered name.
    /// Client-only components are not part of it, so both sides end with the same numbers.
    /// </summary>
    private void BuildNetworkedComponents()
    {
        _networkedTypes.Clear();
        _networkedIds.Clear();

        foreach (var (name, type) in ComponentsSanitized.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            var hasNetAttr = type.GetCustomAttribute<NetworkedComponentAttribute>() is not null;

            if (!hasNetAttr)
            {
                if (GetNetworkedMembers(type).Any())
                    throw new Exception($"{type.FullName} has [NetworkedField] members but is missing [NetworkedComponent].");
                continue;
            }

            _networkedTypes.Add(type);
            _networkedIds[type] = _networkedTypes.Count;
        }

        // the hash covers the names, order and field types so a client and server can compare and agree
        var sb = new StringBuilder();
        foreach (var type in _networkedTypes)
        {
            sb.Append(GetSanitizedByType(type)).Append('{');
            foreach (var member in GetNetworkedMembers(type))
                sb.Append(member.Name).Append(':').Append(GetMemberType(member).FullName).Append(';');
            sb.Append('}');
        }

        _networkedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        Log.Debug($"Networked components: {_networkedTypes.Count}, hash: {_networkedHash}");
    }

    /// <summary>
    /// The [NetField] members of a component, ordered by name.
    /// The getter/setter rules are already enforced at compile time by the generator.
    /// </summary>
    private static IEnumerable<MemberInfo> GetNetworkedMembers(Type type)
        => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<NetFieldAttribute>() is not null)
            .OrderBy(m => m.Name, StringComparer.Ordinal);

    private static Type GetMemberType(MemberInfo member)
        => member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;

    /// <summary>
    /// Every component type that is replicated. The index + 1 is its net id.
    /// </summary>
    public IReadOnlyList<Type> NetworkedTypes => _networkedTypes;

    /// <summary>
    /// Hash of the networked components list (names and fields). Server and client must have the same.
    /// </summary>
    public string GetNetworkedHash() => _networkedHash;

    public bool IsNetworked(Type type)
        => _networkedIds.ContainsKey(type);

    /// <summary>
    /// The id of a networked component type over the network. 0 if the type is not networked.
    /// </summary>
    public int GetNetId(Type type)
        => _networkedIds.GetValueOrDefault(type);

    public int GetNetId<T>() where T : Component
        => GetNetId(typeof(T));

    /// <summary>
    /// The networked component type of a net id. Null if unknown.
    /// </summary>
    public Type? GetTypeByNetId(int netId)
        => netId >= 1 && netId <= _networkedTypes.Count ? _networkedTypes[netId - 1] : null;

    public Type? GetTypeByString(string str)
    {
        if (!Components.ContainsKey(str))
            return null;
        
        return Components[str];
    }

    public string? GetSanitizedByType(Type type)
    {
        foreach (var kvp in ComponentsSanitized)
        {
            if (kvp.Value == type)
                return kvp.Key;
        }

        return null;
    }

    public string? GetSanitizedByType<T>() where T : Component
        => GetSanitizedByType(typeof(T));

    public Component? CreateInstanceFromSanitized(string name)
    {
        if (!ComponentsSanitized.TryGetValue(name, out var type))
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
