using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

[RegisterComponent("Sprite")]
public class SpriteComponent : Component
{
    // devnote: everything that is here that sprite2d already have is for easily prototype component serialization.
    public string Key { get; set; } = string.Empty;

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
    /// Do not set or get this manually. Use SpriteSystem.GetSprite().
    /// </summary>
    public Sprite2D? Spr { get; set; }

    /// <summary>
    /// Do not set this manually. Use SpriteSystem.SetShader().
    /// </summary>
    public string? Shader { get; set; }

    /// <summary>
    /// Do not set this manually. Use SpriteSystem.SetShader().
    /// </summary>
    public Effect? Effect { get; set; }

    public List<SpriteLayer> Layers { get; set; } = new();
}

public sealed class SpriteLayer
{
    /// <summary>
    /// Used to identify layers in the SpriteSystem api - e.g. ID = Clotching (for a clotching layer)
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Define the sprite layer order in the component.
    /// </summary>
    public int Order { get; set; } = 0;

    public bool Visible { get; set; } = true;    
    public string Key { get; set; } = string.Empty;
    public Color Color { get; set; } = Color.White;
    public Vector2? Origin { get; set; }
    public SamplerState? SamplerState { get; set; }

    /// <summary>
    /// Do not set or get this manually. Use SpriteSystem.GetSprite().
    /// </summary>
    public Sprite2D? Spr { get; set; }

    /// <summary>
    /// Do not set this manually. Use SpriteSystem.SetShader().
    /// </summary>
    public string? Shader { get; set; }

    /// <summary>
    /// Do not set this manually. Use SpriteSystem.SetShader().
    /// </summary>
    public Effect? Effect { get; set; }
}
