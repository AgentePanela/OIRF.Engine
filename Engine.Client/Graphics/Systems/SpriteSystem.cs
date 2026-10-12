using System;
using System.Collections.Generic;
using Engine.Client.Assets;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.Containers;
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
    [Dependency] private readonly IRenderManager _renderMan = default!;
    [Dependency] private readonly IAssetManager _assetMan = default!;

    private const string PlaceholderKey = "EngineInternal/Placeholders/Null";

    public bool FreezeDraw { get; set; } = false;

    protected override bool LayersAreLocal => true;

    private readonly Dictionary<EntityUid, SpriteRenderData> _render = new();

    private sealed class SpriteRenderData
    {
        public readonly SpriteTarget Base = new();
        public readonly Dictionary<string, SpriteTarget> Layers = new();

        public bool HideBaseSprite;
        public float DrawRotation;
        public float? DrawDepth;
        public Color? DrawShaderColor;

        public string? DrawShader;
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

    public void Draw(in DrawContext ctx)
    {
        var camera = ctx.Camera;
        var query = GetEntitiesWithComp<SpriteComponent, TransformComponent>();
        foreach ((var uid, var comp, var transform) in query)
        {
            if (!transform.Visible || HasComp<ContainedComponent>(uid))
                continue;

            var data = GetData(uid);
            var sprite = GetSprite(comp, data);
            if (sprite is null)
                continue;

            var spr = sprite.Value;
            var pos = transform.Position + comp.Offset;
            if (!camera.IsOnScreen(spr, pos) && !LayersOnScreen(camera, comp, data, transform, pos))
            {
                _renderMan.ReportCulled();
                continue;
            }

            UpdateSpriteFields(comp, transform, ref spr);
            spr.Rotation += data.DrawRotation;
            spr.Depth = data.DrawDepth ?? spr.Depth;
            SubmitWithLayers(comp, data, transform, spr, pos);
        }
    }

    /// <summary>
    /// Layers can draw away from the base sprite (an offset, or a bigger texture), so one of them may be on screen
    /// while the base is not.
    /// </summary>
    private bool LayersOnScreen(Camera2D camera, SpriteComponent comp, SpriteRenderData data, TransformComponent transform, Vector2 pos)
    {
        if (comp.Layers is null || comp.Layers.Count == 0)
            return false;

        if (comp.LayerReachDirty)
            MeasureLayerReach(comp, data);

        var scale = transform.Scale ?? Vector2.One;
        var reach = new Vector2(comp.LayerReach * MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y)));
        return camera.IsOnScreen(pos - reach, reach * 2f);
    }

    // the furthest a layer could get in any direction: its offset plus its whole size, wherever its origin is
    private void MeasureLayerReach(SpriteComponent comp, SpriteRenderData data)
    {
        var reach = 0f;
        foreach (var layer in comp.Layers)
        {
            if (!layer.Visible || GetLayerSprite(layer, comp, GetLayerTarget(data, layer.Id)) is not { } spr)
                continue;

            reach = MathF.Max(reach, layer.Offset.Length() + MathF.Max(spr.Width, spr.Height));
        }

        comp.LayerReach = reach;
        comp.LayerReachDirty = false;
    }

    /// <summary>
    /// Submits the base sprite and its layers. Base sits at Order 0: layers with
    /// a negative Order draw under it, the rest over.
    /// </summary>
    private void SubmitWithLayers(SpriteComponent comp, SpriteRenderData data, TransformComponent transform, Sprite2D spr, Vector2 pos)
    {
        var shader = ResolveShader(data.Base, data.DrawShader ?? comp.Shader).Effect;
        ApplyDrawColor(shader, data);

        if (comp.Layers is null || comp.Layers.Count == 0)
        {
            SubmitBase(data, spr, pos, shader);
            return;
        }

        if (comp.LayersDirty)
            SortLayers(comp);

        var baseSubmitted = false;
        foreach (var layer in comp.Layers)
        {
            if (!baseSubmitted && layer.Order >= 0)
            {
                SubmitBase(data, spr, pos, shader);
                baseSubmitted = true;
            }
            DrawLayer(comp, data, transform, layer, pos);
        }

        if (!baseSubmitted)
            SubmitBase(data, spr, pos, shader);
    }

    private void SubmitBase(SpriteRenderData data, Sprite2D spr, Vector2 pos, Effect? shader)
    {
        if (!data.HideBaseSprite)
            _renderMan.Submit(spr, pos, shader);
    }

    private static void SortLayers(SpriteComponent comp)
    {
        foreach (var layer in comp.Layers)
            layer.Owner = comp;
        comp.Layers.Sort((a, b) => a.Order.CompareTo(b.Order));
        comp.LayersDirty = false;
    }

    private void DrawLayer(SpriteComponent comp, SpriteRenderData data, TransformComponent trans, SpriteLayer layer, Vector2 pos)
    {
        if (!layer.Visible) return;

        var target = GetLayerTarget(data, layer.Id);
        var sprite = GetLayerSprite(layer, comp, target);
        if (sprite is null)
            return;

        var spr = sprite.Value;
        UpdateLayerFields(layer, trans, comp, ref spr);
        spr.Rotation += data.DrawRotation;
        spr.Depth = data.DrawDepth ?? spr.Depth;

        if (layer.Offset != Vector2.Zero)
            pos += Vector2.Transform(layer.Offset, Matrix.CreateRotationZ(trans.Angle + data.DrawRotation));

        var shader = ResolveShader(target, data.DrawShader ?? layer.Shader).Effect;
        ApplyDrawColor(shader, data);
        _renderMan.Submit(spr, pos, shader);
    }

    // only the shader set through SetDrawShader gets it, a shader that has no such parameter just ignores it
    private static void ApplyDrawColor(Effect? shader, SpriteRenderData data)
    {
        if (shader is null || data.DrawShader is null || data.DrawShaderColor is not { } color)
            return;

        shader.Parameters["DrawColor"]?.SetValue(color.ToVector4());
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
        if (target.DrawKey == key)
            return;

        target.DrawKey = key;

        // another key can be another size
        if (layerId is not null && TryComp<SpriteComponent>(uid, out var comp))
            comp.LayerReachDirty = true;
    }

    /// <summary>
    /// The key set through <see cref="SetDrawKey"/> for the sprite (or a layer), null when its own key is the one drawn.
    /// </summary>
    public string? GetDrawKey(EntityUid uid, string? layerId = null)
    {
        if (!_render.TryGetValue(uid, out var data))
            return null;

        if (layerId is null)
            return data.Base.DrawKey;

        return data.Layers.TryGetValue(layerId, out var target) ? target.DrawKey : null;
    }

    /// <summary>
    /// Stops drawing the base sprite, leaving only the layers.
    /// </summary>
    public void SetBaseHidden(EntityUid uid, bool hidden)
        => GetData(uid).HideBaseSprite = hidden;

    /// <summary>
    /// Rotates the sprite and its layers by <paramref name="radians"/> - nothing replicates and the transform is left alone. Set it back to 0 to stop.
    /// </summary>
    public void SetDrawRotation(EntityUid uid, float radians)
        => GetData(uid).DrawRotation = radians;

    /// <summary>
    /// Draws the sprite and its layers at <paramref name="depth"/> instead of the component own until it is set
    /// back to null.
    /// </summary>
    public void SetDrawDepth(EntityUid uid, float? depth)
    {
        // nothing to undo for a sprite that was never drawn with an override
        if (depth is null && !_render.ContainsKey(uid))
            return;

        GetData(uid).DrawDepth = depth;
    }

    /// <summary>
    /// The depth the sprite is drawn at: the one set through <see cref="SetDrawDepth"/>, or the component's own.
    /// </summary>
    public float GetDrawDepth(SpriteComponent comp)
        => _render.TryGetValue(comp.Owner, out var data) && data.DrawDepth is { } depth ? depth : comp.Depth;

    /// <summary>
    /// Draws the sprite and its layers with <paramref name="shader"/> instead of their own until it is set back to
    /// null. (local)
    /// </summary>
    public void SetDrawShader(EntityUid uid, string? shader, Color? color = null)
    {
        // nothing to undo for a sprite that was never drawn with an override
        if (shader is null && !_render.ContainsKey(uid))
            return;

        var data = GetData(uid);
        data.DrawShader = shader;
        data.DrawShaderColor = color;
    }

    /// <summary>
    /// The shader set through <see cref="SetDrawShader"/>, null when the sprite uses its own.
    /// </summary>
    public string? GetDrawShader(EntityUid uid)
        => _render.TryGetValue(uid, out var data) ? data.DrawShader : null;

    /// <summary>
    /// Get the shader instance of a layer or base sprite.
    /// </summary>
    public Effect? GetDrawShaderEffect(EntityUid uid, string? layerId = null)
    {
        if (!TryComp<SpriteComponent>(uid, out var comp))
            return null;

        var data = GetData(uid);
        if (layerId is null)
            return ResolveShader(data.Base, data.DrawShader ?? comp.Shader).Effect;

        if (GetLayer(comp, layerId) is not { } layer)
            return null;

        return ResolveShader(GetLayerTarget(data, layerId), data.DrawShader ?? layer.Shader).Effect;
    }

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
