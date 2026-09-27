using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Lighting;

/// <summary>
/// Represents a point light source.
/// </summary>
[RegisterComponent("PointLight"), NetworkedComponent, AutoDirty]
public sealed partial class PointLightComponent : Component, IRadialLight
{
    [NetField] public partial Color Color { get; set; } = Color.White;
    [NetField] public partial float Radius { get; set; } = 256f;
    [NetField] public partial float Intensity { get; set; } = 1f;

    /// <summary>
    /// When true, the light is occluded by world geometry and casts shadows.
    /// </summary>
    [NetField] public partial bool CastShadows { get; set; } = true;

    /// <summary>
    /// Local offset relative to the entity transform.
    /// </summary>
    [NetField] public partial Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// Falloff curve. Quadratic is the most physical, linear is faster.
    /// </summary>
    [NetField] public partial FalloffMode Falloff { get; set; } = FalloffMode.Quadratic;

    /// <summary>
    /// Softness multiplier for the shadow PCF kernel. 0 = hard edges
    /// (single-sample), 1 = default soft penumbra, larger = wider.
    /// Combined with <see cref="LightingManager.LightSoftness"/>.
    /// </summary>
    [NetField] public partial float Softness { get; set; } = 1.0f;
}

public enum FalloffMode
{
    Linear,
    Quadratic,
    InverseSquare,
}