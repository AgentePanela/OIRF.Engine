using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;

namespace Engine.Shared.Graphics;

/// <summary>
/// Controls everything about a sprite that is only data.
/// </summary>
public abstract class SharedSpriteSystem : EntitySystem
{
    /// <summary>
    /// Whether layers made through this system belong to this side only.
    /// </summary>
    protected virtual bool LayersAreLocal => false;

    public override void Init()
    {
        base.Init();
        SubscribeEvent<SpriteComponent, CompAddedEvent>(OnSpriteAdded);
    }

    private void OnSpriteAdded(EntityUid uid, SpriteComponent comp, CompAddedEvent args)
    {
        foreach (var layer in comp.Layers)
            layer.Owner = comp;
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
        layer.Local = LayersAreLocal;
        comp.Layers.Add(layer);
        comp.OnLayerSetChanged();
        return layer;
    }

    public bool RemoveLayer(SpriteComponent comp, string id)
    {
        var layer = GetLayer(comp, id);
        if (layer is null)
            return false;

        comp.Layers.Remove(layer);
        comp.OnLayerSetChanged();
        return true;
    }

    /// <summary>
    /// Puts this sprite above everything else on the same Layer.
    /// </summary>
    public void BringToFront(SpriteComponent comp)
    {
        comp.Depth = float.MaxValue;
    }

    /// <summary>
    /// Puts this sprite below everything else on the same Layer.
    /// </summary>
    public void SendToBack(SpriteComponent comp)
    {
        comp.Depth = float.MinValue;
    }
}
