using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Lighting;

/// <summary>
/// Represents a point light source.
/// </summary>
[RegisterComponent("PointLight"), NetworkedComponent]
public sealed partial class PointLightComponent : Component, IRadialLight
{
    [NetField] public Color Color { get; set; } = Color.White;
    [NetField] public float Radius { get; set; } = 256f;
    [NetField] public float Intensity { get; set; } = 1f;

    /// <summary>
    /// When true, the light is occluded by world geometry and casts shadows.
    /// </summary>
    [NetField] public bool CastShadows { get; set; } = true;

    /// <summary>
    /// Local offset relative to the entity transform.
    /// </summary>
    [NetField] public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// Falloff curve. Quadratic is the most physical, linear is faster.
    /// </summary>
    [NetField] public FalloffMode Falloff { get; set; } = FalloffMode.Quadratic;

    /// <summary>
    /// Softness multiplier for the shadow PCF kernel. 0 = hard edges
    /// (single-sample), 1 = default soft penumbra, larger = wider.
    /// Combined with <see cref="LightingManager.LightSoftness"/>.
    /// </summary>
    [NetField] public float Softness { get; set; } = 1.0f;
}

public enum FalloffMode
{
    Linear,
    Quadratic,
    InverseSquare,
}