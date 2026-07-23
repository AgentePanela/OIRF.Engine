using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Engine.Shared;
using Engine.Shared.Assets;
using Engine.Shared.IoC;

namespace Engine.Shared.Prototypes;

public interface IPrototypeManager
{
    public ResPath ResPath { get; }
    internal void Load();

    /// <summary>
    /// Pumps hot-reload of prototype files changed on disk.
    /// </summary>
    void Update();

    /// <summary>
    /// Index a prototype by its typed ID. Throws if not found.
    /// </summary>
    T Index<T>(ProtoId<T> id) where T : class, IPrototype;

    /// <summary>
    /// Try to index a prototype by its typed ID.
    /// </summary>
    bool TryIndex<T>(ProtoId<T> id, [NotNullWhen(true)] out T? proto) where T : class, IPrototype;

    /// <summary>
    /// Returns true if a non-abstract prototype of the given type and ID exists.
    /// </summary>
    bool HasIndex<T>(ProtoId<T> id) where T : class, IPrototype;

    /// <summary>
    /// Enumerate all non-abstract prototypes of a given type.
    /// </summary>
    IEnumerable<T> EnumerateAll<T>() where T : class, IPrototype;

    /// <summary>
    /// Get a read-only snapshot of all non-abstract prototypes of a given type, keyed by ID.
    /// </summary>
    IReadOnlyDictionary<string, T> GetAll<T>() where T : class, IPrototype;

    /// <summary>
    /// Count of non-abstract prototypes of a given type.
    /// </summary>
    int Count<T>() where T : class, IPrototype;

    /// <summary>
    /// Total count of all non-abstract prototypes across all types.
    /// </summary>
    int Count();

    /// <summary>
    /// Returns all registered prototype CLR types.
    /// </summary>
    IEnumerable<Type> GetRegisteredTypes();

    /// <summary>
    /// Returns all registered YAML type keys (e.g. "entity", "randomWeights").
    /// </summary>
    IEnumerable<string> GetRegisteredTypeKeys();

    /// <summary>
    /// Returns true if the given CLR type is a registered prototype type.
    /// </summary>
    bool HasType<T>() where T : class, IPrototype;

    /// <summary>
    /// Returns true if the given YAML type key is registered.
    /// </summary>
    bool HasType(string typeKey);

    void IgnorePrototypes(string[] prototypesToIgnore);
}

public sealed partial class PrototypeManager : IPrototypeManager
{
    [Dependency] private readonly SharedContentManager _contentMan = default!;
    public ResPath ResPath {get; private set; } = new ("Prototypes");
    public List<string> IgnoredPrototypesTypes = new();

    // "entity" -> typeof(EntityPrototype), etc.
    private readonly Dictionary<string, Type> _typeMapping = new(StringComparer.OrdinalIgnoreCase);

    // Grouped raw prototypes: typeKey -> (id -> raw).
    private readonly Dictionary<string, Dictionary<string, RawPrototype>> _rawByType = new(StringComparer.OrdinalIgnoreCase);

    // Typed index: CLR type -> (ID -> instance). Non-abstract only.
    private readonly Dictionary<Type, Dictionary<string, IPrototype>> _index = new();

    // Cached merged fields per (typeKey, id) — avoids re-walking inheritance.
    private readonly Dictionary<(string TypeKey, string Id), Dictionary<string, object>> _mergedCache = new();

    // Cache: typeKey -> set of field names that have [ComponentsDataField].
    private readonly Dictionary<string, HashSet<string>> _componentFields = new(StringComparer.OrdinalIgnoreCase);

    void IPrototypeManager.Load()
    {
        IoCManager.ResolveDependencies(this);

        _typeMapping.Clear();
        _rawByType.Clear();
        _index.Clear();
        _mergedCache.Clear();
        _componentFields.Clear();

        ScanPrototypeTypes();
        foreach (var dir in ResPath.GetFolders())
            LoadRawPrototypes(dir);

        BuildAll();

        InitHotReload();
    }

    public void IgnorePrototypes(string[] prototypesToIgnore)
    {
        IgnoredPrototypesTypes.AddRange(prototypesToIgnore);
    }

    private void ScanPrototypeTypes()
    {
        foreach (var assembly in _contentMan.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IPrototype).IsAssignableFrom(type))
                    continue;

                var attr = type.GetCustomAttribute<PrototypeAttribute>();
                if (attr is null)
                {
                    Log.Warn($"'{type.FullName}' implements IPrototype but has no [Prototype] attribute.");
                    continue;
                }

                if (_typeMapping.TryGetValue(attr.Type, out var existing))
                    throw new PrototypeLoadException(
                        $"Duplicate prototype type '{attr.Type}': '{existing.FullName}' vs '{type.FullName}'.");

                _typeMapping[attr.Type] = type;

                // Discover which DataField names have [ComponentsDataField] for this type.
                var compFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (member, dfAttr) in DataFieldConverter.GetDataFieldMembers(type))
                {
                    if (member.GetCustomAttribute<ComponentsDataFieldAttribute>() is not null)
                        compFieldNames.Add(dfAttr.Name ?? member.Name);
                }

                if (compFieldNames.Count > 0)
                    _componentFields[attr.Type] = compFieldNames;
            }
        }

        Log.Debug($"Registered {_typeMapping.Count} prototype type(s).");
    }

    private void LoadRawPrototypes(string dir)
    {
        var loader = new PrototypeLoader();

        foreach (var raw in loader.LoadProtos(dir, IgnoredPrototypesTypes))
        {
            if (string.IsNullOrWhiteSpace(raw.Type))
                throw new PrototypeLoadException($"Prototype in '{raw.SourceFile}' is missing 'type'.");

            if (string.IsNullOrWhiteSpace(raw.ID))
                throw new PrototypeLoadException($"Prototype in '{raw.SourceFile}' is missing 'id'.");

            if (!_typeMapping.ContainsKey(raw.Type))
            {
                if (!IgnoredPrototypesTypes.Contains(raw.Type))
                    throw new PrototypeLoadException($"Unknown prototype type '{raw.Type}' in '{raw.SourceFile}'.");

                continue;
            }
                
            if (!_rawByType.TryGetValue(raw.Type, out var bucket))
            {
                bucket = new Dictionary<string, RawPrototype>(StringComparer.OrdinalIgnoreCase);
                _rawByType[raw.Type] = bucket;
            }

            if (bucket.ContainsKey(raw.ID))
                throw new PrototypeLoadException(
                    $"Duplicate prototype id '{raw.ID}' (type '{raw.Type}') in '{raw.SourceFile}'.");

            bucket[raw.ID] = raw;
        }
    }

    private void BuildAll()
    {
        var sorted = _typeMapping
            .Select(kvp => (Key: kvp.Key, Priority: kvp.Value.GetCustomAttribute<PrototypeAttribute>()!.LoadPriority))
            .OrderBy(x => x.Priority);

        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (typeKey, _) in sorted)
        {
            if (!_rawByType.TryGetValue(typeKey, out var bucket))
                continue;

            foreach (var id in bucket.Keys)
                Build(typeKey, id, stack);
        }
    }

    private IPrototype Build(string typeKey, string id, HashSet<string> stack)
    {
        // Check if already built in the typed index.
        var clrType = _typeMapping[typeKey];
        if (_index.TryGetValue(clrType, out var typeDict) && typeDict.TryGetValue(id, out var cached))
            return cached;

        if (!_rawByType.TryGetValue(typeKey, out var bucket) || !bucket.TryGetValue(id, out var raw))
            throw new PrototypeLoadException($"Unknown prototype '{id}' of type '{typeKey}'.");

        var mergedFields = GetMergedFields(typeKey, raw, stack);
        var instance = (IPrototype)Activator.CreateInstance(clrType)!;
        PopulateInstance(instance, raw, mergedFields);

        if (instance is not IInheritingPrototype inh || !inh.Abstract)
            AddToIndex(instance);

        return instance;
    }

    /// <summary>
    /// Recursively compute merged fields for a prototype (parent fields + own fields).
    /// Results are cached so each prototype's fields are computed exactly once.
    /// </summary>
    private Dictionary<string, object> GetMergedFields(string typeKey, RawPrototype raw, HashSet<string> stack)
    {
        var cacheKey = (typeKey, raw.ID);
        if (_mergedCache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (!stack.Add(raw.ID))
            throw new PrototypeLoadException($"Cyclic inheritance detected at prototype '{raw.ID}'.");

        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (raw.Parents is not null)
        {
            foreach (var parentId in raw.Parents)
            {
                if (!_rawByType.TryGetValue(typeKey, out var bucket) || !bucket.TryGetValue(parentId, out var parentRaw))
                    throw new PrototypeLoadException($"Prototype '{raw.ID}' references missing parent '{parentId}'.");

                var parentFields = GetMergedFields(typeKey, parentRaw, stack);
                MergeInto(typeKey, result, parentFields);
            }
        }

        MergeInto(typeKey, result, raw.Fields);

        stack.Remove(raw.ID);
        _mergedCache[cacheKey] = result;
        return result;
    }

    /// <summary>
    /// Shallow merge source into target.
    /// Fields marked with [ComponentsDataField] get special merge-by-type-name treatment.
    /// </summary>
    private void MergeInto(string typeKey, Dictionary<string, object> target, Dictionary<string, object> source)
    {
        _componentFields.TryGetValue(typeKey, out var compFields);

        foreach (var (key, value) in source)
        {
            if (compFields is not null
                && compFields.Contains(key)
                && value is List<object> sourceComps)
            {
                if (!target.TryGetValue(key, out var existing) || existing is not List<object> targetComps)
                {
                    targetComps = new List<object>();
                    target[key] = targetComps;
                }

                MergeComponentLists(targetComps, sourceComps);
                continue;
            }

            target[key] = value;
        }
    }

    private static void MergeComponentLists(List<object> target, List<object> source)
    {
        foreach (var item in source)
        {
            if (item is not Dictionary<string, object> srcDict)
                throw new PrototypeLoadException("Component entry must be a YAML mapping with a 'type' field.");

            if (!srcDict.TryGetValue("type", out var typeObj))
                throw new PrototypeLoadException("Component entry is missing 'type'.");

            var typeName = typeObj.ToString()!;

            // Find existing component with same type name.
            Dictionary<string, object>? existingDict = null;
            foreach (var e in target)
            {
                if (e is Dictionary<string, object> d
                    && d.TryGetValue("type", out var t)
                    && string.Equals(t.ToString(), typeName, StringComparison.OrdinalIgnoreCase))
                {
                    existingDict = d;
                    break;
                }
            }

            if (existingDict is null)
            {
                // Clone so we don't share references with cached parent data.
                target.Add(new Dictionary<string, object>(srcDict, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                // Child fields override parent fields.
                foreach (var (k, v) in srcDict)
                    existingDict[k] = v;
            }
        }
    }

    private void PopulateInstance(IPrototype instance, RawPrototype raw, Dictionary<string, object> mergedFields)
    {
        var knownFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "type", "id", "abstract", "parent"
        };

        foreach (var (member, attr) in DataFieldConverter.GetDataFieldMembers(instance.GetType()))
        {
            var fieldName = attr.Name ?? member.Name;
            knownFields.Add(fieldName);

            // Resolve value: built-in meta fields from RawPrototype, rest from merged fields.
            object? rawValue;
            if (string.Equals(fieldName, "type", StringComparison.OrdinalIgnoreCase))
                rawValue = raw.Type;
            else if (string.Equals(fieldName, "id", StringComparison.OrdinalIgnoreCase))
                rawValue = raw.ID;
            else if (string.Equals(fieldName, "abstract", StringComparison.OrdinalIgnoreCase))
                rawValue = raw.Abstract;
            else if (string.Equals(fieldName, "parent", StringComparison.OrdinalIgnoreCase))
                rawValue = raw.Parents is { Length: 1 } ? raw.Parents[0] : (object?)raw.Parents;
            else if (!mergedFields.TryGetValue(fieldName, out rawValue))
            {
                if (attr.Required)
                    throw new PrototypeLoadException(
                        $"Missing required field '{fieldName}' on prototype '{raw.ID}' ({raw.SourceFile}).");
                continue;
            }

            if (rawValue is null)
                continue;

            try
            {
                var converted = DataFieldConverter.Convert(DataFieldConverter.GetMemberType(member), rawValue);
                DataFieldConverter.SetMemberValue(member, instance, converted);
            }
            catch (PrototypeLoadException) { throw; }
            catch (Exception ex)
            {
                throw new PrototypeLoadException(
                    $"Error setting '{fieldName}' on prototype '{raw.ID}' ({raw.SourceFile}): {ex.Message}", ex);
            }
        }
        foreach (var key in raw.Fields.Keys)
        {
            if (!knownFields.Contains(key))
                Log.Warn($"Unknown field '{key}' on prototype '{raw.ID}' ({raw.SourceFile}).");
        }
    }

    private void AddToIndex(IPrototype proto)
    {
        var type = proto.GetType();

        if (!_index.TryGetValue(type, out var dict))
        {
            dict = new Dictionary<string, IPrototype>(StringComparer.OrdinalIgnoreCase);
            _index[type] = dict;
        }

        dict[proto.ID] = proto;
    }

    // interface implementation

    public T Index<T>(ProtoId<T> id) where T : class, IPrototype
    {
        if (!TryIndex(id, out T? proto))
            throw new PrototypeLoadException($"Prototype '{id}' of type '{typeof(T).Name}' not found.");
        return proto;
    }

    public bool TryIndex<T>(ProtoId<T> id, [NotNullWhen(true)] out T? proto) where T : class, IPrototype
    {
        proto = null;
        if (!_index.TryGetValue(typeof(T), out var dict))
            return false;
        if (!dict.TryGetValue(id.Id, out var found))
            return false;
        proto = (T)found;
        return true;
    }

    public bool HasIndex<T>(ProtoId<T> id) where T : class, IPrototype
        => _index.TryGetValue(typeof(T), out var dict) && dict.ContainsKey(id.Id);

    public IEnumerable<T> EnumerateAll<T>() where T : class, IPrototype
    {
        if (!_index.TryGetValue(typeof(T), out var dict))
            yield break;
        foreach (var proto in dict.Values)
            yield return (T)proto;
    }

    public IReadOnlyDictionary<string, T> GetAll<T>() where T : class, IPrototype
    {
        if (!_index.TryGetValue(typeof(T), out var dict))
            return ReadOnlyDictionary<string, T>.Empty;

        var result = new Dictionary<string, T>(dict.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, proto) in dict)
            result[id] = (T)proto;
        return result;
    }

    public int Count<T>() where T : class, IPrototype
        => _index.TryGetValue(typeof(T), out var dict) ? dict.Count : 0;

    public int Count()
        => _index.Values.Sum(dict => dict.Count);

    public IEnumerable<Type> GetRegisteredTypes()
        => _typeMapping.Values;

    public IEnumerable<string> GetRegisteredTypeKeys()
        => _typeMapping.Keys;

    public bool HasType<T>() where T : class, IPrototype
        => _typeMapping.ContainsValue(typeof(T));

    public bool HasType(string typeKey)
        => _typeMapping.ContainsKey(typeKey);
}
