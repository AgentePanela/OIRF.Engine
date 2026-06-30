using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects.Components.Lighting;

/// <summary>
/// Represents a point light source. Emits light in a radial pattern from the
/// owning entity's transform position. Supports color, radius, intensity and
/// optional shadow casting via <c>CastShadows</c>.
/// </summary>
[RegisterComponent("PointLight")]
public sealed class PointLightComponent : Component, IRadialLight
{
    public Color Color { get; set; } = Color.White;
    public float Radius { get; set; } = 256f;
    public float Intensity { get; set; } = 1f;

    /// <summary>
    /// When true, the light is occluded by world geometry and casts shadows.
    /// </summary>
    public bool CastShadows { get; set; } = true;

    /// <summary>
    /// Local offset relative to the entity transform.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// Falloff curve. Quadratic is the most physical, linear is faster.
    /// </summary>
    public FalloffMode Falloff { get; set; } = FalloffMode.Quadratic;

    /// <summary>
    /// Softness multiplier for the shadow PCF kernel. 0 = hard edges
    /// (single-sample), 1 = default soft penumbra, larger = wider.
    /// Combined with <see cref="LightingManager.LightSoftness"/>.
    /// </summary>
    public float Softness { get; set; } = 1.0f;
}

public enum FalloffMode
{
    Linear,
    Quadratic,
    InverseSquare,
}