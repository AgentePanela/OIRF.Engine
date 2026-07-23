using System;
using System.Collections.Generic;
using Engine.Client.Assets;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

public sealed class SpriteSystem : EntityDrawSystem
{
    [Dependency] private readonly RenderManager _renderMan = default!;
    [Dependency] private readonly IAssetManager _assetMan = default!;
    [Dependency] private readonly Camera2D _camera = default!;

    public override void Init()
    {
        base.Init();
    }

    public override void Update(float dt)
    {
        base.Update(dt);
    }

    public override void Draw(float dt)
    {
        base.Draw(dt);

        var query = GetEntitiesWithComp<SpriteComponent, TransformComponent>();
        foreach ((var uid, var comp, var transform) in query)
        {
            if (!transform.Visible)
                continue;

            var sprite = GetSprite(comp);
            if (sprite is null)
                continue;

            var spr = sprite.Value;
            if (!_camera.IsOnScreen(spr, transform.Position))
                continue;

            UpdateSpriteFields(comp, transform, ref spr);
            SubmitWithLayers(comp, transform, spr);
        }
    }

    /// <summary>
    /// Submits the base sprite and its layers. Base sits at Order 0: layers with
    /// a negative Order draw under it, the rest over.
    /// </summary>
    private void SubmitWithLayers(SpriteComponent comp, TransformComponent transform, Sprite2D spr)
    {
        if (comp.Layers is null || comp.Layers.Count == 0)
        {
            _renderMan.Submit(spr, transform.Position, comp.Shader.Effect);
            return;
        }

        if (comp.LayersDirty)
            SortLayers(comp);

        var baseSubmitted = false;
        foreach (var layer in comp.Layers)
        {
            if (!baseSubmitted && layer.Order >= 0)
            {
                _renderMan.Submit(spr, transform.Position, comp.Shader.Effect);
                baseSubmitted = true;
            }
            DrawLayer(comp, transform, layer);
        }

        if (!baseSubmitted)
            _renderMan.Submit(spr, transform.Position, comp.Shader.Effect);
    }

    private static void SortLayers(SpriteComponent comp)
    {
        foreach (var layer in comp.Layers)
            layer.Owner = comp;
        comp.Layers.Sort((a, b) => a.Order.CompareTo(b.Order));
        comp.LayersDirty = false;
    }

    // keeping for compatibility?
    private void DrawBaseLayer(SpriteComponent comp, TransformComponent trans)
    {
        var sprite = GetSprite(comp);
        if (sprite is null)
            return;
               
        var spr = sprite.Value;
        UpdateSpriteFields(comp, trans, ref spr);
        if (!_camera.IsOnScreen(spr, trans.Position))
            return;

        _renderMan.Submit(spr, trans.Position, comp.Shader.Effect);
    }

    private void DrawLayer(SpriteComponent comp, TransformComponent trans, SpriteLayer layer)
    {
        if (!layer.Visible) return;

        var sprite = GetLayerSprite(layer, comp);
        if (sprite is null)
            return;

        var spr = sprite.Value;
        UpdateLayerFields(layer, trans, comp, ref spr);
        _renderMan.Submit(spr, trans.Position, layer.Shader.Effect);
    }

    /// <summary>
    /// Returns the Sprite2D class of a sprite component
    /// </summary>
    public Sprite2D? GetSprite(SpriteComponent comp)
    {
        // fast path - sprite already cached and key hasnt changed
        if (comp.Spr is not null)
            return ValidateSprite(comp);

        // Slow path - first resolution (only happens once per sprite lifecycle)
        var trans = Transform(comp.Owner);
        if (trans is null)
            return null;

        if (!_assetMan.GetSprite(comp.Key, out var sprite))
        {
            Log.Warn($"Unknow sprite key '{comp.Key}' for entity UID {comp.Owner}");
            comp.Key = "EngineInternal/Placeholders/Null";
        }

        // Cache atlas data for fast rendering (eliminates per-frame dictionary lookup)
        CacheAtlasData(comp.Key, ref sprite);

        UpdateSpriteFields(comp, trans, ref sprite);

        comp.Spr = sprite; // cache sprite
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

    private Sprite2D? ValidateSprite(SpriteComponent comp)
    {
        if (comp.Spr?.Key == comp.Key)
            return comp.Spr.Value;

        comp.Spr = null; // comp sprite key has changed, so we need to define to null so getsprite will return the new sprite.
        return GetSprite(comp);
    }

    /// <summary>
    /// get the layer sprite.
    /// </summary>
    public Sprite2D? GetLayerSprite(SpriteLayer layer, SpriteComponent comp)
    {
        // fast path - sprite already cached and key hasnt changed
        if (layer.Spr is not null)
            return ValidateLayerSprite(layer, comp);

        // Slow path: first resolution
        var trans = Transform(comp.Owner);
        if (trans is null)
            return null;

        if (!_assetMan.GetSprite(layer.Key, out var sprite))
        {
            Log.Warn($"Unknown sprite layer key '{layer.Key}' for entity UID {comp.Owner}");
            layer.Key = "EngineInternal/Placeholders/Null";
        }

        // Cache atlas data for fast rendering
        CacheAtlasData(layer.Key, ref sprite);

        UpdateLayerFields(layer, trans, comp, ref sprite);

        layer.Spr = sprite; // cache
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
        sprite.Depth = comp.Spr!.Value.Depth;
        sprite.Visible = layer.Visible;
        sprite.Effects = comp.Effects;
        if (layer.Origin is not null)
            sprite.Origin = layer.Origin.Value;
    }

    private Sprite2D? ValidateLayerSprite(SpriteLayer layer, SpriteComponent comp)
    {
        if (layer.Spr?.Key == layer.Key)
            return layer.Spr.Value;

        layer.Spr = null;
        return GetLayerSprite(layer, comp);
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

    public SpriteLayer? GetLayer(SpriteComponent comp, string id)
    {
        if (comp.Layers is null)
            return null;

        for (int i = 0; i < comp.Layers.Count; i++)
        {
            if (comp.Layers[i].Id == id)
                return comp.Layers[i];
        }
        return null;
    }

    public SpriteLayer AddLayer(SpriteComponent comp, string layerId, string sprKey, int order)
    {
        var layer = new SpriteLayer();
        layer.Id = layerId;
        layer.Key = sprKey;
        layer.Order = order;
        return AddLayer(comp, layer);
    }

    public SpriteLayer AddLayer(SpriteComponent comp, SpriteLayer layer)
    {
        comp.Layers ??= new List<SpriteLayer>();
        if (string.IsNullOrEmpty(layer.Id))
            layer.Id = Guid.NewGuid().ToString();

        layer.Owner = comp;
        comp.Layers.Add(layer);
        comp.LayersDirty = true;

        return layer;
    }

    public bool RemoveLayer(SpriteComponent comp, string id)
    {
        var layer = GetLayer(comp, id);
        if (layer is null)
            return false;

        comp.Layers.Remove(layer);
        return true;
    }

    /// <summary>
    /// Puts this sprite above everything else on the same Layer.
    /// </summary>
    public void BringToFront(SpriteComponent comp) 
        => comp.Depth = float.MaxValue;

    /// <summary>
    /// Puts this sprite below everything else on the same Layer.
    /// </summary>
    public void SendToBack(SpriteComponent comp) 
        => comp.Depth = float.MinValue;
}
