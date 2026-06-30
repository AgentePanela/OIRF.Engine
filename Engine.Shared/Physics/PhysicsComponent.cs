using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Physics;

[RegisterComponent("Physics")]
public sealed class PhysicsComponent : Component
{
    /// <summary>
    /// Current velocity in world units per second.
    /// Applied to the transform every frame by PhysicsSystem.
    /// Movement systems (player, AI) can write here or move the transform directly —
    /// but not both, to avoid double-moving.
    /// </summary>
    public Vector2 Velocity { get; set; } = Vector2.Zero;

    /// <summary>
    /// If true, this entity is never moved by collision resolution.
    /// The full push is transferred to the other body.
    /// </summary>
    public bool Static { get; set; } = false;

    /// <summary>
    /// Mass in kg. 
    /// todo: Reserved for future impulse-based resolution.
    /// </summary>
    public float Mass { get; set; } = 1f;

    /// <summary>
    /// Linear drag applied to Velocity every frame (0 = no drag, 1 = instant stop).
    /// Simulates ground friction without a full physics engine.
    /// </summary>
    public float Friction { get; set; } = 0.3f;

    /// <summary>
    /// Bounciness (0 = no bounce, 1 = perfect bounce). 
    /// todo: Reserved for future use.
    /// </summary>
    public float Restitution { get; set; } = 0f;
}
