using Engine.Shared.GameObjects;

/// <summary>
/// A entity system with capabilities of do Draw() calls. Exclusive to Client-Side.
/// </summary>
public interface IEntityDrawSystem
{
    /// <summary>
    /// Stops the draw calls in this system.
    /// </summary>
    bool FreezeDraw { get; set; }

    /// <summary>
    /// Called every frame to render the system.
    /// Should contain rendering logic only.
    /// </summary>
    void Draw(float dt);
}

/// <inheritdoc cref="IEntityDrawSystem"/>
[IgnoreSystemRegistry]
public abstract class EntityDrawSystem : EntitySystem, IEntityDrawSystem
{
    public bool FreezeDraw { get; set; } = false;

    public virtual void Draw(float dt)
    {
    }
}
