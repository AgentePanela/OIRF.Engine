using Engine.Shared.IoC;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Engine.Client.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Engine.Shared;
using Engine.Shared.Assets;
using Engine.Shared.Graphics;
using Engine.Shared.Prototypes;
using Engine.Shared.Storage;

namespace Engine.Client.Graphics.Shaders;

/// <summary>
/// Manages every compiled shader (Shaders/*.fx) and hands out <see cref="ShaderInstance"/>s for the shader prototypes
/// that use them.
/// </summary>
public sealed class ShaderManager
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private ContentManager _content;
    private Dictionary<string, Effect> _effects = new();

    // by prototype id, null for an id that could not be resolved so it is only reported once
    private readonly Dictionary<ProtoId<ShaderPrototype>, ShaderInstance?> _shared = new();

    public ResPath ShadersResPath = new("Shaders");

    /// <summary>
    /// Draws like it has no shader and lightning.
    /// </summary>
    public ShaderInstance UnshadedInstance { get; }

    private ShaderInstance? _maskInstance;
    private ShaderInstance? _defaultInstance;
    private ShaderInstance? _lightInstance;
    private ShaderInstance? _fovInstance;
    private ShaderInstance? _glowInstance;
    private ShaderInstance? _bloomExtractInstance;
    private ShaderInstance? _bloomBlurInstance;
    private ShaderInstance? _bloomAddInstance;

    /// <summary>
    /// What a sprite with no shader of its own is drawn with while it writes a mask.
    /// </summary>
    internal ShaderInstance? MaskInstance
        => _maskInstance ??= CreateEngineInstance("EngineSpriteMask", lit: false, ShaderBlend.Alpha);

    /// <summary>
    /// What a sprite with no shader of its own is drawn with: the sprite as it is, lit.
    /// </summary>
    internal ShaderInstance? DefaultInstance
        => _defaultInstance ??= CreateEngineInstance("DefaultLit", lit: true, ShaderBlend.Alpha);

    /// <summary>
    /// What one light is drawn into the light map with.
    /// </summary>
    internal ShaderInstance? LightInstance
        => _lightInstance ??= CreateEngineInstance("LightShape", lit: false, ShaderBlend.Additive);

    internal ShaderInstance? FovInstance
        => _fovInstance ??= CreateEngineInstance("Fov", lit: false, ShaderBlend.Alpha);

    internal ShaderInstance? GlowInstance
        => _glowInstance ??= CreateEngineInstance("SpriteGlow", lit: false, ShaderBlend.Additive);

    internal ShaderInstance? BloomExtractInstance
        => _bloomExtractInstance ??= CreateEngineInstance("BloomExtract", lit: false, ShaderBlend.Alpha);

    internal ShaderInstance? BloomBlurInstance
        => _bloomBlurInstance ??= CreateEngineInstance("BloomBlur", lit: false, ShaderBlend.Alpha);

    internal ShaderInstance? BloomAddInstance
        => _bloomAddInstance ??= CreateEngineInstance("BloomAdd", lit: false, ShaderBlend.Additive);

    private ShaderInstance? CreateEngineInstance(string file, bool lit, ShaderBlend blend)
        => _effects.TryGetValue(file, out var effect)
            ? new ShaderInstance(null, effect, lit, blend, null, owned: false, this)
            : null;

    /// <summary>
    /// How many instances made by <see cref="ShaderInstance.Duplicate"/> are alive.
    /// </summary>
    public int OwnedInstances { get; private set; }

    public int SharedInstances => _shared.Count;

    public ShaderManager()
    {
        IoCManager.ResolveDependencies(this);
        UnshadedInstance = new ShaderInstance(null, null, lit: false, ShaderBlend.Alpha, null, owned: false, this);
    }

    internal void Init()
    {
        _content = GameClient.Content;
        Scan(_content.RootDirectory);
    }

    private void Scan(string root)
    {
        if (!FileSystem.DirectoryExists(root))
            return;

        var dir = FileSystem.GetFiles(root, "*.xnb");
        foreach (var file in dir)
        {
            var relativePath = Path.GetRelativePath(_content.RootDirectory, file);
            var assetName = Path.ChangeExtension(relativePath, null);
            assetName = assetName.Replace("\\", "/");
            var name = Path.GetFileNameWithoutExtension(file);

            var shader = _content.Load<Effect>(assetName);
            _effects.Add(name, shader);
        }
    }

    /// <summary>
    /// Gets a unique clone of a shared instance.
    /// </summary>
    public ShaderInstance? GetUniqueInstance(ProtoId<ShaderPrototype>? id)
    {
        if (GetSharedInstance(id) is not { } instance)
            return null;
        
        return instance.Duplicate();
    }

    /// <summary>
    /// The instance everything using the shader prototype <paramref name="id"/> shares.
    /// </summary>
    public ShaderInstance? GetSharedInstance(ProtoId<ShaderPrototype>? id)
    {
        if (id is not { Id: not null } proto)
            return null;

        if (_shared.TryGetValue(proto, out var instance))
            return instance;

        instance = Create(proto);
        _shared[proto] = instance;
        return instance;
    }

    private ShaderInstance? Create(ProtoId<ShaderPrototype> id)
    {
        if (!_proto.TryIndex(id, out var proto))
        {
            Log.Warn($"Unknown shader prototype '{id}'");
            return null;
        }

        if (!_effects.TryGetValue(proto.File, out var source))
        {
            Log.Warn($"Shader prototype '{id}' uses '{proto.File}.fx', which is not a compiled shader");
            return null;
        }

        var slots = new Dictionary<string, SpriteParamSlot>();
        if (proto.PerSprite is not null)
        {
            foreach (var (name, where) in proto.PerSprite)
            {
                if (ShaderInstance.TryParseSlot(where, out var slot))
                    slots[name] = slot;
                else
                    Log.Warn($"Shader prototype '{id}' sends '{name}' to '{where}', expected custom0 or custom1 (like custom1.x)");
            }
        }

        // its own copy of the effect, so two prototypes on the same .fx can start from different values
        var instance = new ShaderInstance(id, source.Clone(), proto.Lit, proto.Blend, slots, owned: false, this);

        if (proto.Lit && !instance.ReadsLight)
            Log.Warn($"Shader prototype '{id}' is lit, but '{proto.File}.fx' does not read the light (it does not end with SPRITE_TECHNIQUE), so the light never darkens it");

        if (proto.Params is not null)
        {
            var custom0 = Vector4.Zero;
            var custom1 = Vector4.Zero;

            foreach (var (name, raw) in proto.Params)
            {
                // a per sprite one is not on the effect, it is what sprites start with
                if (slots.TryGetValue(name, out var slot))
                {
                    if (ShaderInstance.TryParseValue(raw, out var start))
                        ShaderInstance.WriteSlot(ref custom0, ref custom1, slot, start);
                    else
                        Log.Warn($"Shader prototype '{id}' has a value for '{name}' that is not a number or a list of up to 4");
                }
                else if (!instance.HasParameter(name))
                    Log.Warn($"Shader prototype '{id}' sets '{name}', which '{proto.File}.fx' does not have");
                else if (ShaderInstance.TryParseValue(raw, out var value))
                    instance.SetParameter(name, value);
                else
                    Log.Warn($"Shader prototype '{id}' has a value for '{name}' that is not a number or a list of up to 4");
            }

            instance.DefaultCustom0 = custom0;
            instance.DefaultCustom1 = custom1;
        }

        return instance;
    }

    internal void CountOwned(int delta)
        => OwnedInstances += delta;

    /// <summary>
    /// The compiled effect of a .fx file, by file name. This is the instance the shader was loaded into: clone it
    /// before changing anything on it, or use <see cref="GetSharedInstance"/>.
    /// </summary>
    public Effect? GetShader(string? name)
    {
        if (name is null)
            return null;

        _effects.TryGetValue(name, out var effect);

        return effect;
    }

    /// <summary>
    /// Whether <paramref name="shaderName"/> is a shader prototype.
    /// </summary>
    public bool HasShader(ProtoId<ShaderPrototype>? shaderName)
    {
        if (shaderName is not { Id: not null } proto)
            return false;

        return _proto.HasIndex(proto);
    }

    /// <summary>
    /// The id of every shader prototype.
    /// </summary>
    public List<ProtoId<ShaderPrototype>> GetShaderList()
        => _proto.EnumerateAll<ShaderPrototype>().Select(p => new ProtoId<ShaderPrototype>(p.ID)).ToList();
}
