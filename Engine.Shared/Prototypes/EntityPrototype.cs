using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;

namespace Engine.Shared.Prototypes;

/// <summary>
/// Prototype definition for entities. Supports inheritance and component data merging.
/// </summary>
[Prototype("entity")]
public sealed class EntityPrototype : IPrototype, IInheritingPrototype
{
    [DataField("type", required: true)]
    public string Type { get; private set; } = "entity";

    [DataField("id", required: true)]
    public string ID { get; private set; } = default!;

    [DataField("parent")]
    public string[]? Parents { get; private set; }

    [DataField("abstract")]
    public bool Abstract { get; private set; }

    [DataField("name")]
    public string? Name { get; private set; }

    [DataField("components")]
    [ComponentsDataField]
    public List<object> RawComponents { get; private set; } = new();

    /// <summary>
    /// Parsed component entries, keyed by component type name for fast lookup.
    /// Built lazily from RawComponents after deserialization.
    /// </summary>
    private Dictionary<string, ComponentEntry>? _components;

    public Dictionary<string, ComponentEntry> Components
    {
        get
        {
            if (_components is null)
                _components = BuildComponentEntries();
            return _components;
        }
    }

    private Dictionary<string, ComponentEntry> BuildComponentEntries()
    {
        var result = new Dictionary<string, ComponentEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in RawComponents)
        {
            if (item is not Dictionary<string, object> dict)
                throw new PrototypeLoadException(
                    $"Component entry in prototype '{ID}' must be a YAML mapping.");

            if (!dict.TryGetValue("type", out var typeObj))
                throw new PrototypeLoadException(
                    $"Component entry in prototype '{ID}' is missing 'type' field.");

            var typeName = typeObj.ToString()!;
            var entry = new ComponentEntry { Type = typeName };

            foreach (var (k, v) in dict)
            {
                if (string.Equals(k, "type", StringComparison.OrdinalIgnoreCase))
                    continue;

                entry.Data[k] = v;
            }

            result[typeName] = entry;
        }

        return result;
    }

    /// <summary>
    /// Returns whether this prototype contains a component with the given type name.
    /// </summary>
    public bool HasComponent(string typeName)
        => Components.ContainsKey(typeName);

    /// <summary>
    /// Tries to retrieve a component entry by type name.
    /// </summary>
    public bool TryGetComponentEntry(string typeName, [NotNullWhen(true)] out ComponentEntry? entry)
        => Components.TryGetValue(typeName, out entry);

    /// <summary>
    /// Gets a component entry by type name. Throws if not found.
    /// </summary>
    public ComponentEntry GetComponentEntry(string typeName)
    {
        if (!Components.TryGetValue(typeName, out var entry))
            throw new KeyNotFoundException(
                $"Entity prototype '{ID}' does not contain a component of type '{typeName}'.");
        return entry;
    }
}

/// <summary>
/// Represents a single component's data within an EntityPrototype.
/// </summary>
public sealed class ComponentEntry
{
    /// <summary>
    /// The registered component type name (e.g. "Transform", "Sprite").
    /// </summary>
    public string Type { get; set; } = default!;

    /// <summary>
    /// Key-value pairs of component data fields.
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Indexer shortcut. Returns the raw value or null if the key is missing.
    /// </summary>
    public object? this[string key]
        => Data.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// Returns whether a data field exists.
    /// </summary>
    public bool Has(string key)
        => Data.ContainsKey(key);

    /// <summary>
    /// Tries to read a data field and cast it to <typeparamref name="T"/>.
    /// Returns false if the key is missing or the value cannot be cast.
    /// </summary>
    public bool TryGet<T>(string key, [NotNullWhen(true)] out T? value)
    {
        value = default;

        if (!Data.TryGetValue(key, out var raw) || raw is null)
            return false;

        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        // Attempt conversion for common numeric / string mismatches.
        try
        {
            value = (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
            return value is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets a string field, or <paramref name="defaultValue"/> if missing.
    /// </summary>
    public string GetString(string key, string defaultValue = "")
        => TryGet<string>(key, out var v) ? v : defaultValue;

    /// <summary>
    /// Gets an integer field, or <paramref name="defaultValue"/> if missing or unparseable.
    /// </summary>
    public int GetInt(string key, int defaultValue = 0)
    {
        if (!Data.TryGetValue(key, out var raw) || raw is null)
            return defaultValue;

        if (raw is int i) return i;
        if (raw is long l) return (int)l;
        if (raw is double d) return (int)d;
        if (raw is float f) return (int)f;

        return int.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    /// <summary>
    /// Gets a float field, or <paramref name="defaultValue"/> if missing or unparseable.
    /// </summary>
    public float GetFloat(string key, float defaultValue = 0f)
    {
        if (!Data.TryGetValue(key, out var raw) || raw is null)
            return defaultValue;

        if (raw is float f) return f;
        if (raw is double d) return (float)d;
        if (raw is int i) return i;
        if (raw is long l) return l;

        return float.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    /// <summary>
    /// Gets a boolean field, or <paramref name="defaultValue"/> if missing or unparseable.
    /// </summary>
    public bool GetBool(string key, bool defaultValue = false)
    {
        if (!Data.TryGetValue(key, out var raw) || raw is null)
            return defaultValue;

        if (raw is bool b) return b;

        return bool.TryParse(raw.ToString(), out var parsed)
            ? parsed
            : defaultValue;
    }
}
