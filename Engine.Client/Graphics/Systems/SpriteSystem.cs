using System.Collections.Generic;
using Engine.Client.Assets;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.GameObjects;
using Engine.Shared.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// Resolves and draws <see cref="SpriteComponent"/>s. The component is only data (it replicates), so everything that
/// exists just to draw it lives in here, per entity.
/// </summary>
public sealed class SpriteSystem : SharedSpriteSystem, IEntityDrawSystem
{
    [Dependency] private readonly RenderManager _renderMan = default!;
    [Dependency] private readonly IAssetManager _assetMan = default!;
    [Dependency] private readonly Camera2D _camera = default!;

    private const string PlaceholderKey = "EngineInternal/Placeholders/Null";

    public bool FreezeDraw { get; set; } = false;

    protected override bool LayersAreLocal => true;

    private readonly Dictionary<EntityUid, SpriteRenderData> _render = new();

    private sealed class SpriteRenderData
    {
        public readonly SpriteTarget Base = new();
        public readonly Dictionary<string, SpriteTarget> Layers = new();

        public bool HideBaseSprite;
    }

    /// <summary>
    /// What gets drawn for the base sprite or one layer.
    /// </summary>
    private sealed class SpriteTarget
    {
        public Sprite2D? Sprite;

        /// <summary>
        /// The key <see cref="Sprite"/> was resolved for.
        /// </summary>
        public string? ResolvedFor;

        /// <summary>
        /// Overrides the component's key while set
        /// </summary>
        public string? DrawKey;

        public string? ShaderName;
        public ShaderPath Shader;
    }

    public override void Init()
    {
        base.Init();
        SubscribeEvent<SpriteComponent, CompRemovedEvent>(OnSpriteRemoved);
    }

    private void OnSpriteRemoved(EntityUid uid, SpriteComponent comp, CompRemovedEvent args)
        => _render.Remove(uid);

    public void Draw(float dt)
    {
        var query = GetEntitiesWithComp<SpriteComponent, TransformComponent>();
        foreach ((var uid, var comp, var transform) in query)
        {
            if (!transform.Visible)
                continue;

            var data = GetData(uid);
            var sprite = GetSprite(comp, data);
            if (sprite is null)
                continue;

            var spr = sprite.Value;
            if (!_camera.IsOnScreen(spr, transform.Position))
                continue;

            UpdateSpriteFields(comp, transform, ref spr);
            SubmitWithLayers(comp, data, transform, spr);
        }
    }

    /// <summary>
    /// Submits the base sprite and its layers. Base sits at Order 0: layers with
    /// a negative Order draw under it, the rest over.
    /// </summary>
    private void SubmitWithLayers(SpriteComponent comp, SpriteRenderData data, TransformComponent transform, Sprite2D spr)
    {
        var shader = ResolveShader(data.Base, comp.Shader).Effect;

        if (comp.Layers is null || comp.Layers.Count == 0)
        {
            SubmitBase(data, spr, transform, shader);
            return;
        }

        if (comp.LayersDirty)
            SortLayers(comp);

        var baseSubmitted = false;
        foreach (var layer in comp.Layers)
        {
            if (!baseSubmitted && layer.Order >= 0)
            {
                SubmitBase(data, spr, transform, shader);
                baseSubmitted = true;
            }
            DrawLayer(comp, data, transform, layer);
        }

        if (!baseSubmitted)
            SubmitBase(data, spr, transform, shader);
    }

    private void SubmitBase(SpriteRenderData data, Sprite2D spr, TransformComponent transform, Effect? shader)
    {
        if (!data.HideBaseSprite)
            _renderMan.Submit(spr, transform.Position, shader);
    }

    private static void SortLayers(SpriteComponent comp)
    {
        foreach (var layer in comp.Layers)
            layer.Owner = comp;
        comp.Layers.Sort((a, b) => a.Order.CompareTo(b.Order));
        comp.LayersDirty = false;
    }

    private void DrawLayer(SpriteComponent comp, SpriteRenderData data, TransformComponent trans, SpriteLayer layer)
    {
        if (!layer.Visible) return;

        var target = GetLayerTarget(data, layer.Id);
        var sprite = GetLayerSprite(layer, comp, target);
        if (sprite is null)
            return;

        var spr = sprite.Value;
        UpdateLayerFields(layer, trans, comp, ref spr);

        var pos = trans.Position;
        if (layer.Offset != Vector2.Zero)
            pos += Vector2.Transform(layer.Offset, Matrix.CreateRotationZ(trans.Angle));

        _renderMan.Submit(spr, pos, ResolveShader(target, layer.Shader).Effect);
    }

    /// <summary>
    /// Returns the Sprite2D class of a sprite component
    /// </summary>
    public Sprite2D? GetSprite(SpriteComponent comp)
        => GetSprite(comp, GetData(comp.Owner));

    private Sprite2D? GetSprite(SpriteComponent comp, SpriteRenderData data)
    {
        var target = data.Base;
        var key = target.DrawKey ?? comp.Key;

        // fast path - sprite already cached and key hasnt changed
        if (target.Sprite is not null && target.ResolvedFor == key)
            return target.Sprite.Value;

        // Slow path - first resolution, or the key changed
        var trans = Transform(comp.Owner);
        if (trans is null)
            return null;

        var sprite = Resolve(key, out _, comp.Owner, null);

        UpdateSpriteFields(comp, trans, ref sprite);

        target.Sprite = sprite;
        target.ResolvedFor = key;
        return sprite;
    }

    /// <summary>
    /// Same as <see cref="GetSprite(SpriteComponent)"/> but with updated properties.
    /// </summary>
    public Sprite2D? GetLiveSprite(SpriteComponent comp, TransformComponent trans)
    {
        var sprite = GetSprite(comp);
        if (sprite is null)
            return null;

        var spr = sprite.Value;
        UpdateSpriteFields(comp, trans, ref spr);
        return spr;
    }

    private void UpdateSpriteFields(SpriteComponent comp, TransformComponent trans, ref Sprite2D sprite)
    {
        sprite.Rotation = trans.Angle;
        sprite.Visible = trans.Visible;
        sprite.Layer = comp.Layer;
        sprite.Depth = comp.Depth;
        sprite.SamplerState = comp.SamplerState;
        sprite.Scale = trans.Scale ?? Vector2.One;
        sprite.Color = comp.Color;
        sprite.Effects = comp.Effects;
        if (comp.Origin is not null)
            sprite.Origin = comp.Origin.Value;
    }

    /// <summary>
    /// get the layer sprite.
    /// </summary>
    public Sprite2D? GetLayerSprite(SpriteLayer layer, SpriteComponent comp)
        => GetLayerSprite(layer, comp, GetLayerTarget(GetData(comp.Owner), layer.Id));

    private Sprite2D? GetLayerSprite(SpriteLayer layer, SpriteComponent comp, SpriteTarget target)
    {
        var key = target.DrawKey ?? layer.Key;

        // fast path - sprite already cached and key hasnt changed
        if (target.Sprite is not null && target.ResolvedFor == key)
            return target.Sprite.Value;

        // Slow path: first resolution, or the key changed
        var trans = Transform(comp.Owner);
        if (trans is null)
            return null;

        var sprite = Resolve(key, out _, comp.Owner, layer.Id);

        UpdateLayerFields(layer, trans, comp, ref sprite);

        target.Sprite = sprite;
        target.ResolvedFor = key;
        return sprite;
    }

    private void UpdateLayerFields(SpriteLayer layer, TransformComponent trans, SpriteComponent comp, ref Sprite2D sprite)
    {
        sprite.Layer = comp.Layer;
        sprite.Rotation = trans.Angle;
        sprite.Visible = trans.Visible;
        sprite.SamplerState = layer.SamplerState;
        sprite.Scale = trans.Scale ?? Vector2.One;
        sprite.Color = layer.Color;
        sprite.Depth = comp.Depth;
        sprite.Visible = layer.Visible;
        sprite.Effects = comp.Effects;
        if (layer.Origin is not null)
            sprite.Origin = layer.Origin.Value;
    }

    /// <summary>
    /// Builds the Sprite2D for a key, falling back to the placeholder when it does not exist.
    /// </summary>
    private Sprite2D Resolve(string key, out string resolvedKey, EntityUid owner, string? layerId)
    {
        resolvedKey = key;
        if (!_assetMan.GetSprite(key, out var sprite))
        {
            if (layerId is null)
                Log.Warn($"Unknow sprite key '{key}' for entity UID {owner}");
            else
                Log.Warn($"Unknown sprite layer key '{key}' for entity UID {owner}, layer '{layerId}'");

            resolvedKey = PlaceholderKey;
            _assetMan.GetSprite(resolvedKey, out sprite);
        }

        // Cache atlas data for fast rendering (eliminates per-frame dictionary lookup)
        CacheAtlasData(resolvedKey, ref sprite);
        return sprite;
    }

    /// <summary>
    /// Resolves and caches atlas texture/region into the Sprite2D so DrawSprite skips dictionary lookup.
    /// </summary>
    private void CacheAtlasData(string key, ref Sprite2D sprite)
    {
        if (_assetMan.GetTexture(key, out var atlasSpr, out var atlasPage))
        {
            sprite.CachedTexture = atlasPage.Texture;
            sprite.CachedRegion = atlasSpr.Region;
        }
    }

    // resolving a ShaderPath clones the Effect, so only do it when the name changes
    private static ShaderPath ResolveShader(SpriteTarget target, string? name)
    {
        if (target.ShaderName != name)
        {
            target.ShaderName = name;
            target.Shader = new ShaderPath(name);
        }

        return target.Shader;
    }

    /// <summary>
    /// Draws <paramref name="key"/> instead of the sprite's own key (or a layer's, with <paramref name="layerId"/>)
    /// until it is set back to null. This is how animations show their frames.
    /// </summary>
    public void SetDrawKey(EntityUid uid, string? layerId, string? key)
    {
        var data = GetData(uid);
        var target = layerId is null ? data.Base : GetLayerTarget(data, layerId);
        target.DrawKey = key;
    }

    /// <summary>
    /// Stops drawing the base sprite, leaving only the layers.
    /// </summary>
    public void SetBaseHidden(EntityUid uid, bool hidden)
        => GetData(uid).HideBaseSprite = hidden;

    private SpriteRenderData GetData(EntityUid uid)
    {
        if (!_render.TryGetValue(uid, out var data))
            _render[uid] = data = new SpriteRenderData();

        return data;
    }

    private static SpriteTarget GetLayerTarget(SpriteRenderData data, string layerId)
    {
        if (!data.Layers.TryGetValue(layerId, out var target))
            data.Layers[layerId] = target = new SpriteTarget();

        return target;
    }
}
