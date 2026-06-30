using Engine.Shared.GameObjects;

/// <summary>
/// A entity system with capabilities of do Draw() calls. Exclusive to Client-Side.
/// </summary>
public abstract class EntityDrawSystem : EntitySystem
{
    /// <summary>
    /// Called every frame to render the system. 
    /// Should contain rendering logic only. 
    /// </summary>
    public virtual void Draw(float dt) { }
}