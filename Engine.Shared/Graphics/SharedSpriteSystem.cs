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
        comp.LayersDirty = true;

        Dirty(comp);
        return layer;
    }

    public bool RemoveLayer(SpriteComponent comp, string id)
    {
        var layer = GetLayer(comp, id);
        if (layer is null)
            return false;

        comp.Layers.Remove(layer);
        Dirty(comp);
        return true;
    }

    /// <summary>
    /// Puts this sprite above everything else on the same Layer.
    /// </summary>
    public void BringToFront(SpriteComponent comp)
    {
        comp.Depth = float.MaxValue;
        Dirty(comp);
    }

    /// <summary>
    /// Puts this sprite below everything else on the same Layer.
    /// </summary>
    public void SendToBack(SpriteComponent comp)
    {
        comp.Depth = float.MinValue;
        Dirty(comp);
    }
}
