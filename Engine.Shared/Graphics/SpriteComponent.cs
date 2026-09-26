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
    public string Key { get; set; } = "";

    /// <summary>
    /// The layer where this sprite will be drawed
    /// </summary>
    public int Layer { get; set; } = 0;

    /// <summary>
    /// Z-order inside Layer, higher draws on top. See SpriteSystem.BringToFront/SendToBack.
    /// </summary>
    public float Depth { get; set; } = 0f;
    public Color Color { get; set; } = Color.White;
    public Vector2? Origin { get; set; }

    public SamplerState? SamplerState { get; set; }
    public SpriteEffects Effects { get; set; } = SpriteEffects.None;

    /// <summary>
    /// Name of the shader to render this sprite with.
    /// </summary>
    [ShaderKey]
    public string? Shader { get; set; }

    public List<SpriteLayer> Layers { get; set; } = new();

    /// <summary>
    /// set when a layer Order changes so SpriteSystem re-sorts before the next draw.
    /// </summary>
    internal bool LayersDirty = true;

    public override IComponentState? GetNetState(GameTick fromTick)
    {
        var layers = new List<SpriteLayerState>(Layers.Count);
        foreach (var layer in Layers)
        {
            if (!layer.Local)
                layers.Add(SpriteLayerState.From(layer));
        }

        return new SpriteComponentState
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
            Layers = layers.ToArray(),
        };
    }

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
                LayersDirty = true;
            }

            state.ApplyTo(layer);
        }

        for (var i = Layers.Count - 1; i >= 0; i--)
        {
            var layer = Layers[i];
            if (layer.Local || Array.Exists(states, s => s.Id == layer.Id))
                continue;

            Layers.RemoveAt(i);
            LayersDirty = true;
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

    /// <summary>
    /// Used to identify layers in the SpriteSystem api - e.g. ID = Clotching (for a clotching layer)
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    private int _order;

    /// <summary>
    /// Define the sprite layer order in the component.
    /// </summary>
    public int Order
    {
        get => _order;
        set
        {
            if (_order == value)
                return;

            _order = value;
            if (Owner is not null)
                Owner.LayersDirty = true;
        }
    }

    public bool Visible { get; set; } = true;

    [TextureKey]
    public string Key { get; set; } = "";
    
    public Color Color { get; set; } = Color.White;
    public Vector2? Origin { get; set; }
    public SamplerState? SamplerState { get; set; }

    /// <summary>
    /// Offset based on the main layer.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <inheritdoc cref="SpriteComponent.Shader"/>
    [ShaderKey]
    public string? Shader { get; set; }
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
