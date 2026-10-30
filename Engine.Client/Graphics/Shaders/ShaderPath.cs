using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Shaders;

/// <summary>
/// The id of a shader prototype. Use this for components or prototypes giving the possibility to set shader by
/// name in prototypes.
/// </summary>
[TypeConverter(typeof(ShaderPathConverter))]
public readonly struct ShaderPath : IEquatable<ShaderPath>
{
    public readonly string? Name;

    /// <summary>
    /// The shared instance of the shader, or null if unset or not a shader prototype.
    /// </summary>
    public ShaderInstance? Instance => IoCManager.Resolve<ShaderManager>().GetSharedInstance(Name);

    public ShaderPath(string? name)
    {
        Name = name;
    }

    /// <summary>
    /// Every shader name currently loaded
    /// </summary>
    public static IReadOnlyList<string> GetAvailable()
        => IoCManager.Resolve<ShaderManager>().GetShaderList().ConvertAll(id => id.Id);

    public static implicit operator ShaderPath(string? name) => new(name);

    public bool Equals(ShaderPath other) => Name == other.Name;
    public override bool Equals(object? obj) => obj is ShaderPath other && Equals(other);
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;
    public override string ToString() => Name ?? string.Empty;

    public static bool operator ==(ShaderPath left, ShaderPath right) => left.Equals(right);
    public static bool operator !=(ShaderPath left, ShaderPath right) => !left.Equals(right);
}

internal sealed class ShaderPathConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string s ? new ShaderPath(s) : base.ConvertFrom(context, culture, value);
}
