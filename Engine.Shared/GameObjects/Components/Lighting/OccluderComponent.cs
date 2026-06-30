using Engine.Shared.GameObjects;

namespace Engine.Shared.GameObjects.Components.Lighting;

/// <summary>
/// Marks an entity as a light occluder. Any <see cref="PointLightComponent"/>
/// or <see cref="TextureLightComponent"/> with <c>CastShadows = true</c> will
/// cast a ray-cast shadow around this entity.
/// </summary>
[RegisterComponent("Occluder")]
public sealed class OccluderComponent : Component
{
    /// <summary>
    /// Shape of the occluder used for shadow casting.
    /// </summary>
    public OccluderShape Shape { get; set; } = OccluderShape.Sprite;

    /// <summary>
    /// Radius (in world units) of the occluder. Used when
    /// <see cref="Shape"/> is <see cref="OccluderShape.Circle"/>.
    /// </summary>
    public float Radius { get; set; } = 32f;

    /// <summary>
    /// Width/height (in world units) of the occluder. Used when
    /// <see cref="Shape"/> is <see cref="OccluderShape.Rectangle"/>.
    /// </summary>
    public Microsoft.Xna.Framework.Vector2 Size { get; set; } = new(64, 64);
}

public enum OccluderShape
{
    /// <summary>
    /// Use the entity's sprite bounding box as the occluder silhouette.
    /// </summary>
    Sprite,

    /// <summary>
    /// Use a circular footprint centered at the transform.
    /// </summary>
    Circle,

    /// <summary>
    /// Use an axis-aligned rectangle centered at the transform.
    /// </summary>
    Rectangle,
}