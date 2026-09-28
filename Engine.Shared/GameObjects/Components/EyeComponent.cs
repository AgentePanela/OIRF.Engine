using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

/// <summary>
/// A point the world is replicated around for PVS.
/// </summary>
[RegisterComponent("Eye")]
public sealed class EyeComponent : Component
{
    public bool Active { get; set; } = true;

    /// <summary>
    /// How far this eye sees, null for the net.pvs-range cvar default.
    /// </summary>
    public float? Range { get; set; }

    public Vector2 Offset { get; set; } = Vector2.Zero;
}
