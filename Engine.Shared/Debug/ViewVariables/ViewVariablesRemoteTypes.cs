using System;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Debug.ViewVariables;

/// <summary>
/// Turns the type name a remote snapshot carries back into a real <see cref="Type"/>, but only for the types a value
/// can actually be parsed out of text into.
/// </summary>
public static class ViewVariablesRemoteTypes
{
    private static readonly ConcurrentDictionary<string, Type?> Cache = new();

    public static Type? Resolve(string typeName)
        => typeName.Length == 0 ? null : Cache.GetOrAdd(typeName, Lookup);

    private static Type? Lookup(string typeName)
    {
        var type = Find(typeName);
        return type is not null && IsSupported(type) ? type : null;
    }

    private static Type? Find(string typeName)
    {
        // covers System.* and anything whose name carries its assembly inside it, which is the case for Nullable<T>
        if (Type.GetType(typeName) is { } direct)
            return direct;

        // the rest lives in assemblies this one does not reference by name (MonoGame's Vector2/Color, content types)
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetType(typeName) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Whether a value of this type can be rebuilt from its text form. Resolving anything else would only make the
    /// parse throw once per member per refresh for a value no editor would use.
    /// </summary>
    private static bool IsSupported(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        return target.IsPrimitive
            || target.IsEnum
            || target == typeof(string)
            || target == typeof(decimal)
            || target == typeof(EntityUid)
            || target == typeof(Vector2)
            || target == typeof(Vector3)
            || target == typeof(Vector4)
            || target == typeof(Color)
            || target == typeof(Point);
    }
}
