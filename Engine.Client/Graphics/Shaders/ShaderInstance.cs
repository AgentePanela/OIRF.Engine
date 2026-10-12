using System;
using System.Collections.Generic;
using System.Globalization;
using Engine.Shared;
using Engine.Shared.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Shaders;

public readonly record struct SpriteParamSlot(int Register, int Offset, int Count);

/// <summary>
/// A shader instance ready for be drawn. (from the prototype)
/// </summary>
public sealed class ShaderInstance : IDisposable
{
    private static readonly IReadOnlyDictionary<string, SpriteParamSlot> NoSlots = new Dictionary<string, SpriteParamSlot>();

    /// <summary>
    /// The shader prototype id.
    /// </summary>
    public ProtoId<ShaderPrototype>? Prototype { get; }

    /// <summary>
    /// Null draws with the plain sprite effect.
    /// </summary>
    public Effect? Effect { get; }

    /// <summary>
    /// False for what ignores the lighting.
    /// </summary>
    public bool Lit { get; }

    public ShaderBlend Blend { get; }

    /// <summary>
    /// True for an instance made by <see cref="Duplicate"/>. Setting a parameter on a shared will
    /// change for ALL others sprites that are using the shader.
    /// </summary>
    public bool IsOwned { get; }

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// What the two per sprite values start as: the prototype's params that are listed under perSprite.
    /// </summary>
    public Vector4 DefaultCustom0 { get; internal set; }

    /// <inheritdoc cref="DefaultCustom0"/>
    public Vector4 DefaultCustom1 { get; internal set; }

    // filled by the renderer before each draw
    internal readonly EffectParameter? MatrixParam;
    internal readonly EffectParameter? TexelSizeParam;
    internal readonly EffectParameter? TimeParam;
    internal readonly EffectParameter? ScreenSizeParam;

    private readonly IReadOnlyDictionary<string, SpriteParamSlot> _spriteSlots;
    private readonly ShaderManager? _owner;

    internal ShaderInstance(ProtoId<ShaderPrototype>? prototype, Effect? effect, bool lit, ShaderBlend blend,
        IReadOnlyDictionary<string, SpriteParamSlot>? spriteSlots, bool owned, ShaderManager? owner)
    {
        Prototype = prototype;
        Effect = effect;
        Lit = lit;
        Blend = blend;
        IsOwned = owned;
        _spriteSlots = spriteSlots ?? NoSlots;
        _owner = owner;

        if (effect is null)
            return;

        MatrixParam = effect.Parameters["MatrixTransform"];
        TexelSizeParam = effect.Parameters["TexelSize"];
        TimeParam = effect.Parameters["Time"];
        ScreenSizeParam = effect.Parameters["ScreenSize"];
    }

    /// <summary>
    /// A copy with its own parameters, starting from the current ones.
    /// </summary>
    public ShaderInstance Duplicate()
    {
        _owner?.CountOwned(1);
        return new ShaderInstance(Prototype, Effect?.Clone(), Lit, Blend, _spriteSlots, owned: true, _owner)
        {
            DefaultCustom0 = DefaultCustom0,
            DefaultCustom1 = DefaultCustom1,
        };
    }

    /// <summary>
    /// Whether <paramref name="name"/> travels with each sprite instead of being set on the shader.
    /// </summary>
    public bool TryGetSpriteSlot(string name, out SpriteParamSlot slot)
        => _spriteSlots.TryGetValue(name, out slot);

    public bool HasParameter(string name)
        => Effect?.Parameters[name] is not null;

    public void SetParameter(string name, float value)
        => SetParameter(name, new Vector4(value, 0f, 0f, 0f));

    public void SetParameter(string name, Vector2 value)
        => SetParameter(name, new Vector4(value, 0f, 0f));

    public void SetParameter(string name, Vector3 value)
        => SetParameter(name, new Vector4(value, 0f));

    public void SetParameter(string name, Color value)
        => SetParameter(name, value.ToVector4());

    /// <summary>
    /// Sets a parameter of the shader, using as many components as the parameter has. A name the shader does not
    /// have is ignored.
    /// </summary>
    public void SetParameter(string name, Vector4 value)
    {
        if (Effect?.Parameters[name] is not { } param)
            return;

        if (param.ParameterClass == EffectParameterClass.Scalar)
        {
            switch (param.ParameterType)
            {
                case EffectParameterType.Bool:
                    param.SetValue(value.X != 0f);
                    break;
                case EffectParameterType.Int32:
                    param.SetValue((int)value.X);
                    break;
                default:
                    param.SetValue(value.X);
                    break;
            }

            Log.Warn($"Unknow shader param name {name} for instance using {Prototype ?? "NO PROTOTYPE"}");
            return;
        }

        if (param.ParameterClass != EffectParameterClass.Vector)
            return;

        switch (param.ColumnCount)
        {
            case 2:
                param.SetValue(new Vector2(value.X, value.Y));
                break;
            case 3:
                param.SetValue(new Vector3(value.X, value.Y, value.Z));
                break;
            default:
                param.SetValue(value);
                break;
        }
    }

    public void SetParameter(string name, Texture2D? value)
        => Effect?.Parameters[name]?.SetValue(value);

    public void SetParameter(string name, Matrix value)
        => Effect?.Parameters[name]?.SetValue(value);

    public void Dispose()
    {
        // the shared one lives as long as the shader manager
        if (!IsOwned || IsDisposed)
            return;

        IsDisposed = true;
        Effect?.Dispose();
        _owner?.CountOwned(-1);
    }

    /// <summary>
    /// Puts <paramref name="value"/> where <paramref name="slot"/> says, in whichever of the two values it names.
    /// </summary>
    public static void WriteSlot(ref Vector4 custom0, ref Vector4 custom1, SpriteParamSlot slot, Vector4 value)
    {
        ref var register = ref custom0;
        if (slot.Register != 0)
            register = ref custom1;

        for (var i = 0; i < slot.Count; i++)
        {
            var component = i switch { 0 => value.X, 1 => value.Y, 2 => value.Z, _ => value.W };

            switch (slot.Offset + i)
            {
                case 0: register.X = component; break;
                case 1: register.Y = component; break;
                case 2: register.Z = component; break;
                default: register.W = component; break;
            }
        }
    }

    /// <summary>
    /// Reads a "custom0" / "custom1.x" / "custom0.zw" style slot.
    /// </summary>
    internal static bool TryParseSlot(string text, out SpriteParamSlot slot)
    {
        slot = default;
        text = text.Trim().ToLowerInvariant();

        var dot = text.IndexOf('.');
        var register = dot < 0 ? text : text[..dot];
        var swizzle = dot < 0 ? "xyzw" : text[(dot + 1)..];

        int index;
        if (register == "custom0")
            index = 0;
        else if (register == "custom1")
            index = 1;
        else
            return false;

        // components have to follow each other: x, yz, xyz...
        var offset = "xyzw".IndexOf(swizzle, StringComparison.Ordinal);
        if (swizzle.Length == 0 || offset < 0)
            return false;

        slot = new SpriteParamSlot(index, offset, swizzle.Length);
        return true;
    }

    /// <summary>
    /// A prototype value (a number or a list of them) as a vector.
    /// </summary>
    internal static bool TryParseValue(object? raw, out Vector4 value)
    {
        value = default;

        if (raw is null)
            return false;

        if (raw is string || raw is not System.Collections.IEnumerable list)
        {
            if (!TryParseFloat(raw, out var single))
                return false;

            value.X = single;
            return true;
        }

        var i = 0;
        foreach (var item in list)
        {
            if (i >= 4 || !TryParseFloat(item, out var component))
                return false;

            switch (i++)
            {
                case 0: value.X = component; break;
                case 1: value.Y = component; break;
                case 2: value.Z = component; break;
                default: value.W = component; break;
            }
        }

        return i > 0;
    }

    private static bool TryParseFloat(object? raw, out float value)
    {
        value = 0f;
        var text = raw?.ToString();

        if (bool.TryParse(text, out var flag))
        {
            value = flag ? 1f : 0f;
            return true;
        }

        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
