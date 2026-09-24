using Engine.Shared.GameObjects;

namespace Engine.Shared.GameStates;

/// <summary>
/// Raised on the client after a component had a state applied to it. <para/>
/// This is the hook to react to replicated data - <see cref="CompAddedEvent"/> fires before the first state is read.
/// </summary>
public sealed class ComponentStateAppliedEvent : ComponentEvent
{
    /// <summary>
    /// The component had no state applied to it before this one.
    /// </summary>
    public bool FirstState;
}

/// <summary>
/// Raised on the client after every component of an entity in a state has been applied.
/// </summary>
public sealed class EntityStateAppliedEvent : EntityEvent
{
    /// <summary>
    /// The entity was created by this state.
    /// </summary>
    public bool Entering;
}
