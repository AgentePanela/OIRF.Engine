using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Lighting;

/// <summary>
/// Represents a cone-shaped light source.
/// </summary>
[RegisterComponent("SpotLight"), NetworkedComponent, AutoDirty]
public sealed partial class SpotLightComponent : Component, IRadialLight
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
    /// Combined with <see cref="LightingManager"/>'s LightSoftness.
    /// </summary>
    [NetField] public partial float Softness { get; set; } = 1.0f;

    /// <summary>
    /// Direction the cone points, in radians. 0 = +X (right).
    /// Combined with the entity's transform angle when
    /// <see cref="RotatesWithTransform"/> is true.
    /// </summary>
    [NetField] public partial float Direction { get; set; } = 0f;

    /// <summary>
    /// Full angular width of the cone, in degrees. 360 behaves like a
    /// regular point light.
    /// </summary>
    [NetField] public partial float ConeAngle { get; set; } = 45f;

    /// <summary>
    /// When true, <see cref="Direction"/> is relative to the entity
    /// transform's rotation, so rotating the entity rotates the cone.
    /// When false, <see cref="Direction"/> is an absolute world angle.
    /// </summary>
    [NetField] public partial bool RotatesWithTransform { get; set; } = true;
}
