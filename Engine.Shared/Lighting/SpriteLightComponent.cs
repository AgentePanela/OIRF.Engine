using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.Timing;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Lighting;

/// <summary>
/// Layers of the sprite comp that light what is around them (like a tv!), with the color
/// of what they show.<para/>
/// <b>Call dirty after changing a Lights entry.</b>
/// </summary>
[RegisterComponent("SpriteLight"), NetworkedComponent]
public sealed class SpriteLightComponent : Component
{
    public bool Enabled { get; set { field = value; Dirty(); } } = true;

    public List<SpriteLightEntry> Lights { get; set { field = value; Dirty(); } } = new();

    public override IComponentState? GetNetState(GameTick fromTick)
    {
        var lights = new SpriteLightEntryState[Lights.Count];
        for (var i = 0; i < Lights.Count; i++)
            lights[i] = SpriteLightEntryState.From(Lights[i]);

        return new SpriteLightComponentState { Enabled = Enabled, Lights = lights };
    }

    public override void HandleNetState(IComponentState state)
    {
        if (state is not SpriteLightComponentState s)
            return;

        Enabled = s.Enabled;

        var lights = new List<SpriteLightEntry>(s.Lights.Length);
        foreach (var light in s.Lights)
            lights.Add(light.ToEntry());

        Lights = lights;
    }
}

/// <summary>
/// One layer that lights what is around it.
/// </summary>
public sealed class SpriteLightEntry
{
    /// <summary>
    /// The id of the sprite layer.
    /// </summary>
    public string Layer { get; set; } = string.Empty;

    /// <summary>
    /// Multiplies the color of the layer.
    /// </summary>
    public Color Color { get; set; } = Color.White;

    public float Intensity { get; set; } = 1f;

    /// <summary>
    /// How far past the edges of the layer the light reaches, fading out, in world units.
    /// </summary>
    public float Spread { get; set; } = 32f;

    /// <summary>
    /// Moves the light away from the layer.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;
}

[Serializable]
public sealed class SpriteLightComponentState : IComponentState
{
    public bool Enabled;
    public SpriteLightEntryState[] Lights = [];
}

[Serializable]
public sealed class SpriteLightEntryState
{
    public string Layer = "";
    public Color Color;
    public float Intensity;
    public float Spread;
    public Vector2 Offset;

    public static SpriteLightEntryState From(SpriteLightEntry entry) => new()
    {
        Layer = entry.Layer,
        Color = entry.Color,
        Intensity = entry.Intensity,
        Spread = entry.Spread,
        Offset = entry.Offset,
    };

    public SpriteLightEntry ToEntry() => new()
    {
        Layer = Layer,
        Color = Color,
        Intensity = Intensity,
        Spread = Spread,
        Offset = Offset,
    };
}
