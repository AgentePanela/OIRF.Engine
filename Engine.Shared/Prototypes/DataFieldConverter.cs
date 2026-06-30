using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Engine.Shared.Prototypes;

/// <summary>
/// Shared utility for converting raw YAML values into strongly-typed CLR values.
/// Caches reflection results per type for performance.
/// </summary>
public static partial class DataFieldConverter
{
    // Cache: Type -> array of (MemberInfo, DataFieldAttribute) for that type.
    private static readonly ConcurrentDictionary<Type, (MemberInfo Member, DataFieldAttribute Attr)[]> _cache = new();

    /// <summary>
    /// Get all [DataField]-decorated members for a type, cached after first call.
    /// </summary>
    public static (MemberInfo Member, DataFieldAttribute Attr)[] GetDataFieldMembers(Type type)
    {
        if (_cache.TryGetValue(type, out var cached))
            return cached;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var list = new List<(MemberInfo, DataFieldAttribute)>();

        foreach (var member in type.GetMembers(flags))
        {
            var attr = member.GetCustomAttribute<DataFieldAttribute>();
            if (attr is not null)
                list.Add((member, attr));
        }

        var result = list.ToArray();
        _cache[type] = result;
        return result;
    }

    /// <summary>
    /// Apply a dictionary of raw values to all [DataField] members of the target object.
    /// </summary>
    public static void ApplyFields(object target, Dictionary<string, object> data)
    {
        foreach (var (member, attr) in GetDataFieldMembers(target.GetType()))
        {
            var name = attr.Name ?? member.Name;
            if (!data.TryGetValue(name, out var rawValue))
                continue;

            SetMemberValue(member, target, Convert(GetMemberType(member), rawValue));
        }
    }

    /// <summary>
    /// Apply raw key-value pairs to public fields/properties by name (no [DataField] required).
    /// Used for component data where fields aren't annotated with [DataField].
    /// </summary>
    public static void ApplyByName(object target, Dictionary<string, object> data)
    {
        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;

        foreach (var (key, value) in data)
        {
            var prop = type.GetProperty(key, flags);
            if (prop is not null)
            {
                var setter = prop.GetSetMethod(true)
                    ?? throw new PrototypeLoadException($"Property '{type.Name}.{key}' has no setter.");
                setter.Invoke(target, new[] { Convert(prop.PropertyType, value) });
                continue;
            }

            var field = type.GetField(key, flags);
            if (field is not null)
            {
                field.SetValue(target, Convert(field.FieldType, value));
                continue;
            }

            throw new PrototypeLoadException($"Component '{type.Name}' does not contain field/property '{key}'.");
        }
    }

    public static Type GetMemberType(MemberInfo member) => member switch
    {
        PropertyInfo p => p.PropertyType,
        FieldInfo f => f.FieldType,
        _ => throw new PrototypeLoadException($"Unsupported member kind: {member.MemberType}")
    };

    public static object? GetMemberValue(MemberInfo member, object obj) => member switch
    {
        PropertyInfo p => p.GetValue(obj),
        FieldInfo f => f.GetValue(obj),
        _ => null
    };

    public static void SetMemberValue(MemberInfo member, object obj, object? value)
    {
        switch (member)
        {
            case PropertyInfo p:
                var setter = p.GetSetMethod(true)
                    ?? throw new PrototypeLoadException(
                        $"Property '{p.DeclaringType?.Name}.{p.Name}' has no setter.");
                setter.Invoke(obj, [value]);
                break;
            case FieldInfo f:
                f.SetValue(obj, value);
                break;
        }
    }
}
