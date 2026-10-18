using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Lighting;

/// <summary>
/// Marks an entity as a light occluder.
/// </summary>
[RegisterComponent("Occluder"), NetworkedComponent, AutoDirty]
public sealed partial class OccluderComponent : Component
{
    [NetField] public partial bool Enabled { get; set; } = true;

    /// <summary>
    /// Shape of the occluder used for shadow casting.
    /// </summary>
    [NetField] public partial OccluderShape Shape { get; set; } = OccluderShape.Rectangle;

    /// <summary>
    /// Local offset relative to the entity transform. Lets the mask sit off
    /// center.
    /// </summary>
    [NetField] public partial Vector2 Offset { get; set; } = Microsoft.Xna.Framework.Vector2.Zero;

    /// <summary>
    /// Radius (in world units) of the occluder. Used when
    /// <see cref="Shape"/> is <see cref="OccluderShape.Circle"/>.
    /// </summary>
    [NetField] public partial float Radius { get; set; } = 32f;

    /// <summary>
    /// Width/height (in world units) of the occluder. Used when
    /// <see cref="Shape"/> is <see cref="OccluderShape.Rectangle"/>.
    /// </summary>
    [NetField] public partial Vector2 Size { get; set; } = new(64, 64);
}

public enum OccluderShape
{
    /// <summary>
    /// Use the entity sprite bounding box as the occluder silhouette.
    /// </summary>
    Sprite,

    Circle,

    Rectangle,
}