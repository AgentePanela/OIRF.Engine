using Engine.Shared.Assets;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Lighting;

/// <summary>
/// Represents a entity light source.
/// </summary>
[RegisterComponent("PointLight"), NetworkedComponent, AutoDirty]
public sealed partial class PointLightComponent : Component
{
    [NetField] public partial bool Enabled { get; set; } = true;

    [NetField] public partial Color Color { get; set; } = Color.White;

    [NetField] public partial float Radius { get; set; } = 256f;

    [NetField] public partial float Intensity { get; set; } = 1f;

    /// <summary>
    /// When true, the light is occluded by world geometry and casts shadows.
    /// </summary>
    [NetField] public partial bool CastShadows { get; set; } = true;

    /// <summary>
    /// Where the light sits, from the entity position. It is not rotated with the entity.
    /// </summary>
    [NetField] public partial Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// Falloff curve. Quadratic is the most physical, linear is faster.
    /// </summary>
    [NetField] public partial FalloffMode Falloff { get; set; } = FalloffMode.Quadratic;

    /// <summary>
    /// How big the light source is, which is how wide its shadow edges get. 0 = hard edges.
    /// </summary>
    [NetField] public partial float Softness { get; set; } = 1.0f;

    /// <summary>
    /// Texture key whose alpha gives the light its shape, stretched over the whole light.
    /// </summary>
    [TextureKey, NetField] public partial string Mask { get; set; } = string.Empty;

    /// <summary>
    /// Turns the light (its cone and its mask), in radians.
    /// </summary>
    [NetField] public partial float Rotation { get; set; } = 0f;

    /// <summary>
    /// When true <see cref="Rotation"/> is added to the entity rotation, so turning the entity turns the light.
    /// </summary>
    [NetField] public partial bool RotatesWithTransform { get; set; } = true;

    /// <summary>
    /// Full width of the cone the light shines in, in degrees.
    /// </summary>
    [NetField] public partial float ConeAngle { get; set; } = 360f;

    /// <summary>
    /// How much of the cone fades out at its edges: 0 is a hard edge, 1 fades from the middle.
    /// </summary>
    [NetField] public partial float ConeSoftness { get; set; } = 0.25f;
}

public enum FalloffMode
{
    Linear,
    Quadratic,
    InverseSquare,
}
