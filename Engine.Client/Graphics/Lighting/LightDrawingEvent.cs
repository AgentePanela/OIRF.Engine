using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Raised on a PointLight entity every frame before it is drawn. Changes what will be drawn (not the component)
/// </summary>
public sealed class LightDrawingEvent : EntityEvent
{
    public bool Enabled;

    /// <summary>
    /// Position of the light, with its offset.
    /// </summary>
    public Vector2 Center;

    public Color Color;
    public float Radius;
    public float Intensity;

    /// <summary>
    /// added to the rotation of the light.
    /// </summary>
    public float Rotation;

    /// <summary>
    /// Set it when the values change every frame, so the light is not kept in the cache.
    /// </summary>
    public bool Animated;
}
