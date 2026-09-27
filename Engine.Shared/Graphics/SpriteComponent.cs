using System;
using System.Collections.Generic;
using Engine.Shared.Assets;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.Timing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Shared.Graphics;

[RegisterComponent("Sprite"), NetworkedComponent]
public class SpriteComponent : Component
{
    // devnote: everything that is here that sprite2d already have is for easily prototype component serialization.
    [TextureKey]
    public string Key { get; set { field = value; DirtyBase(); } } = "";

    /// <summary>
    /// The layer where this sprite will be drawed
    /// </summary>
    public int Layer { get; set { field = value; DirtyBase(); } } = 0;

    /// <summary>
    /// Z-order inside Layer, higher draws on top. See SpriteSystem.BringToFront/SendToBack.
    /// </summary>
    public float Depth { get; set { field = value; DirtyBase(); } } = 0f;
    public Color Color { get; set { field = value; DirtyBase(); } } = Color.White;
    public Vector2? Origin { get; set { field = value; DirtyBase(); } }

    public SamplerState? SamplerState { get; set { field = value; DirtyBase(); } }
    public SpriteEffects Effects { get; set { field = value; DirtyBase(); } } = SpriteEffects.None;

    /// <summary>
    /// Name of the shader to render this sprite with.
    /// </summary>
    [ShaderKey]
    public string? Shader { get; set { field = value; DirtyBase(); } }

    public List<SpriteLayer> Layers
    {
        get;
        set
        {
            field = value;
            foreach (var layer in field)
                layer.Owner = this;

            OnLayerSetChanged();
        }
    } = new();

    private void DirtyBase()
    {
        BaseTick = EntityManager.Instance?.CurTick ?? GameTick.Zero;
        Dirty();
    }

    internal void OnLayerChanged(bool reordered = false)
    {
        if (reordered)
            LayersDirty = true;

        Dirty();
    }

    internal void OnLayerSetChanged()
    {
        LayerSetTick = EntityManager.Instance?.CurTick ?? GameTick.Zero;
        LayersDirty = true;
        Dirty();
    }

    /// <summary>
    /// set when a layer Order changes so SpriteSystem re-sorts before the next draw.
    /// </summary>
    internal bool LayersDirty = true;

    internal GameTick BaseTick;
    internal GameTick LayerSetTick;

    public override IComponentState? GetNetState(GameTick fromTick)
    {
        if (fromTick == GameTick.Zero)
            return BuildBase(CollectLayers(GameTick.Zero));

        var delta = new SpriteComponentDeltaState
        {
            Base = BaseTick >= fromTick ? BuildBase([]) : null,
            Layers = CollectLayers(fromTick),
        };

        if (LayerSetTick >= fromTick)
            delta.LayerIds = CollectLayerIds();

        return delta;
    }

    private SpriteLayerState[] CollectLayers(GameTick since)
    {
        var layers = new List<SpriteLayerState>(Layers.Count);
        foreach (var layer in Layers)
        {
            if (!layer.Local && layer.LastModifiedTick >= since)
                layers.Add(SpriteLayerState.From(layer));
        }

        return layers.ToArray();
    }

    private string[] CollectLayerIds()
    {
        var ids = new List<string>(Layers.Count);
        foreach (var layer in Layers)
        {
            if (!layer.Local)
                ids.Add(layer.Id);
        }

        return ids.ToArray();
    }

    private SpriteComponentState BuildBase(SpriteLayerState[] layers) => new()
    {
        Key = Key,
        Layer = Layer,
        Depth = Depth,
        Color = Color,
        HasOrigin = Origin is not null,
        Origin = Origin ?? Vector2.Zero,
        Sampler = SamplerPresets.IndexOf(SamplerState),
        Effects = (byte)Effects,
        Shader = Shader,
        Layers = layers,
    };

    public override void HandleNetState(IComponentState state)
    {
        if (state is not SpriteComponentState s)
            return;

        Key = s.Key;
        Layer = s.Layer;
        Depth = s.Depth;
        Color = s.Color;
        Origin = s.HasOrigin ? s.Origin : null;
        SamplerState = SamplerPresets.Get(s.Sampler);
        Effects = (SpriteEffects)s.Effects;
        Shader = s.Shader;

        ApplyLayers(s.Layers);
    }

    private void ApplyLayers(SpriteLayerState[] states)
    {
        foreach (var state in states)
        {
            var layer = FindNetworkLayer(state.Id);
            if (layer is null)
            {
                layer = new SpriteLayer { Owner = this };
                Layers.Add(layer);
                OnLayerSetChanged();
            }

            state.ApplyTo(layer);
        }

        for (var i = Layers.Count - 1; i >= 0; i--)
        {
            var layer = Layers[i];
            if (layer.Local || Array.Exists(states, s => s.Id == layer.Id))
                continue;

            Layers.RemoveAt(i);
            OnLayerSetChanged();
        }
    }

    private SpriteLayer? FindNetworkLayer(string id)
    {
        foreach (var layer in Layers)
        {
            if (!layer.Local && layer.Id == id)
                return layer;
        }

        return null;
    }
}

public sealed class SpriteLayer
{
    internal SpriteComponent? Owner;

    /// <summary>
    /// Made on this client through its SpriteSystem, so states never replace it.
    /// </summary>
    internal bool Local;
    internal GameTick LastModifiedTick;

    /// <summary>
    /// Used to identify layers in the SpriteSystem api - e.g. ID = Clotching (for a clotching layer)
    /// </summary>
    public string Id { get; set { field = value; Dirty(); } } = Guid.NewGuid().ToString();

    /// <summary>
    /// Define the sprite layer order in the component.
    /// </summary>
    public int Order
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            Dirty(reordered: true);
        }
    }

    public bool Visible { get; set { field = value; Dirty(); } } = true;

    [TextureKey]
    public string Key { get; set { field = value; Dirty(); } } = "";
    
    public Color Color { get; set { field = value; Dirty(); } } = Color.White;
    public Vector2? Origin { get; set { field = value; Dirty(); } }
    public SamplerState? SamplerState { get; set { field = value; Dirty(); } }

    /// <summary>
    /// Offset based on the main layer.
    /// </summary>
    public Vector2 Offset { get; set { field = value; Dirty(); } } = Vector2.Zero;

    /// <inheritdoc cref="SpriteComponent.Shader"/>
    [ShaderKey]
    public string? Shader { get; set { field = value; Dirty(); } }

    private void Dirty(bool reordered = false)
    {
        LastModifiedTick = EntityManager.Instance?.CurTick ?? GameTick.Zero;
        Owner?.OnLayerChanged(reordered);
    }
}

[Serializable]
public sealed class SpriteComponentState : IComponentState
{
    public string Key = "";
    public int Layer;
    public float Depth;
    public Color Color;
    public bool HasOrigin;
    public Vector2 Origin;
    public byte Sampler;
    public byte Effects;
    public string? Shader;
    public SpriteLayerState[] Layers = [];

    public SpriteComponentState WithLayers(SpriteLayerState[] layers) => new()
    {
        Key = Key,
        Layer = Layer,
        Depth = Depth,
        Color = Color,
        HasOrigin = HasOrigin,
        Origin = Origin,
        Sampler = Sampler,
        Effects = Effects,
        Shader = Shader,
        Layers = layers,
    };
}

/// <summary>
/// Only what changed since the session first appearance.
/// </summary>
[Serializable]
public sealed class SpriteComponentDeltaState : IComponentDeltaState<SpriteComponentState>
{
    public SpriteComponentState? Base;

    public SpriteLayerState[] Layers = [];

    /// <summary>
    /// Every replicated layer id, sent only when a layer was added or removed. Null when not changed.
    /// </summary>
    public string[]? LayerIds;

    public SpriteComponentState CreateNewFullState(SpriteComponentState full)
    {
        var merged = new List<SpriteLayerState>(full.Layers);

        foreach (var layer in Layers)
        {
            var index = merged.FindIndex(l => l.Id == layer.Id);
            if (index >= 0)
                merged[index] = layer;
            else
                merged.Add(layer);
        }

        if (LayerIds is not null)
            merged.RemoveAll(l => Array.IndexOf(LayerIds, l.Id) < 0);

        return (Base ?? full).WithLayers(merged.ToArray());
    }
}

[Serializable]
public sealed class SpriteLayerState
{
    public string Id = "";
    public int Order;
    public bool Visible;
    public string Key = "";
    public Color Color;
    public bool HasOrigin;
    public Vector2 Origin;
    public Vector2 Offset;
    public byte Sampler;
    public string? Shader;

    public static SpriteLayerState From(SpriteLayer layer) => new()
    {
        Id = layer.Id,
        Order = layer.Order,
        Visible = layer.Visible,
        Key = layer.Key,
        Color = layer.Color,
        HasOrigin = layer.Origin is not null,
        Origin = layer.Origin ?? Vector2.Zero,
        Offset = layer.Offset,
        Sampler = SamplerPresets.IndexOf(layer.SamplerState),
        Shader = layer.Shader,
    };

    public void ApplyTo(SpriteLayer layer)
    {
        layer.Id = Id;
        layer.Order = Order;
        layer.Visible = Visible;
        layer.Key = Key;
        layer.Color = Color;
        layer.Origin = HasOrigin ? Origin : null;
        layer.Offset = Offset;
        layer.SamplerState = SamplerPresets.Get(Sampler);
        layer.Shader = Shader;
    }
}
