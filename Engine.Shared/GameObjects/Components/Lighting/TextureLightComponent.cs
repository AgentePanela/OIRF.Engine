using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects.Components.Lighting;

/// <summary>
/// Represents a light whose shape is defined by a texture. Useful for
/// spotlights, glowing windows, sign panels, custom shaped lights, etc.
/// </summary>
[RegisterComponent("TextureLight")]
public sealed class TextureLightComponent : Component
{
    /// <summary>
    /// Asset key for the light texture
    /// (e.g. <c>"Lights/SpotlightCone"</c>).
    /// </summary>
    public string Texture { get; set; } = string.Empty;

    public Color Color { get; set; } = Color.White;
    public float Intensity { get; set; } = 1f;

    /// <summary>
    /// Local scale of the light texture. (1, 1) = raw size.
    /// </summary>
    public Vector2 Scale { get; set; } = Vector2.One;

    /// <summary>
    /// Local offset relative to the entity transform.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// When true, the light follows the entity rotation.
    /// </summary>
    public bool RotatesWithTransform { get; set; } = true;

    /// <summary>
    /// Extra rotation in radians, applied on top of the entity rotation
    /// when <see cref="RotatesWithTransform"/> is true, or used as the
    /// light's absolute rotation when it is false.
    /// </summary>
    public float Rotation { get; set; } = 0f;

    /// <summary>
    /// When true, the light is occluded by world geometry and casts shadows.
    /// </summary>
    public bool CastShadows { get; set; } = false;
}
