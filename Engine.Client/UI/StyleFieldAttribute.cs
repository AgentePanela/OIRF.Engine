using System;

namespace Engine.Client.UI;

/// <summary>
/// Marks a backing field for the StylePropertyGenerator source generator.
/// It emits a matching
/// public property about that property.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class StyleFieldAttribute : Attribute
{
    /// <summary>
    /// The style property key this field resolves against, e.g. "minWidth".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Fallback used if neither the local value nor the theme has one.
    /// </summary>
    public object? Default { get; }

    public StyleFieldAttribute(string name, object? @default = null)
    {
        Name = name;
        Default = @default;
    }
}
