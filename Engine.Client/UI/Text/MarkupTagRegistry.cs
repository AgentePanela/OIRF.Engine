using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Engine.Client.UI;

/// <summary>
/// Looks up <see cref="IMarkupTag"/> handlers by tag name.
/// </summary>
public static class MarkupTagRegistry
{
    private static readonly Dictionary<string, IMarkupTag> _tags = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(IMarkupTag tag) => _tags[tag.Name] = tag;
    public static bool TryRegister(IMarkupTag tag)
    {
        if (_tags.ContainsKey(tag.Name))
            return false;

        Register(tag);
        return true;
    }

    public static bool TryGet(string name, [NotNullWhen(true)] out IMarkupTag? tag) => _tags.TryGetValue(name, out tag);

    // /// <summary>
    // /// Scans an assembly for a <see cref="IMarkupTag"/> type.
    // /// </summary>
    // at this point i think it is better to use Register()
    // public static void AutoRegister(Assembly assembly)
    // {
    //     foreach (var type in assembly.GetTypes())
    //     {
    //         if (type.IsAbstract || type.IsInterface || !typeof(IMarkupTag).IsAssignableFrom(type))
    //             continue;

    //         if (type.GetConstructor(Type.EmptyTypes) is null)
    //             continue;

    //         TryRegister((IMarkupTag)Activator.CreateInstance(type)!);
    //     }
    // }
}
