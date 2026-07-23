using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Engine.Shared.IoC;

namespace Engine.Client.Assets;

/// <summary>
/// Reserved for future use. Please put this struct in your prototype of component or anything that can be deserializated
/// in yout field designed for a sprite key.
/// </summary>
[TypeConverter(typeof(SpriteKeyConverter))]
public readonly struct SpriteKey : IEquatable<SpriteKey>
{
    public readonly string? Key;

    public SpriteKey(string? key)
    {
        Key = key;
    }

    /// <summary>
    /// Every sprite key currently loaded
    /// </summary>
    public static IReadOnlyList<string> GetAvailable()
        => IoCManager.Resolve<IAssetManager>().GetSpriteKeys();

    public static implicit operator SpriteKey(string? key) => new(key);

    public static implicit operator string(SpriteKey key) => key.Key ?? string.Empty;

    public bool Equals(SpriteKey other) => Key == other.Key;
    public override bool Equals(object? obj) => obj is SpriteKey other && Equals(other);
    public override int GetHashCode() => Key?.GetHashCode() ?? 0;
    public override string ToString() => Key ?? string.Empty;

    // Deliberately no == / != operator overloads: with a bidirectional string conversion,
    // adding them would make `comp.Spr?.Key == comp.Key` (string == SpriteKey) ambiguous
    // between converting the left side to SpriteKey (use SpriteKey==) or the right side to
    // string (use string==). Leaving only the conversions keeps that comparison resolving
    // through the single applicable string== path.
}

internal sealed class SpriteKeyConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string s ? new SpriteKey(s) : base.ConvertFrom(context, culture, value);
}
