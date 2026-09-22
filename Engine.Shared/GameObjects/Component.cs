using Engine.Shared.GameStates;
using Engine.Shared.IoC;
using Engine.Shared.Threading;
using Engine.Shared.Timing;
using Lidgren.Network;

namespace Engine.Shared.GameObjects;

/// <summary>
/// A class that holds data, made to be store into a entity and manipulated by the entity system.
/// </summary>
public class Component
{
    public EntityUid Owner { get; set; } = EntityUid.Empty;

    /// <summary>
    /// The component is marked to be deleted in the next frame.
    /// </summary>
    public bool Deleted { get; internal set; } = false;

    /// <summary>
    /// Represents the current state of the component
    /// </summary>
    public CompState State { get; internal set; } = CompState.Adding;

    /// <summary>
    /// The tick this component was added on.
    /// </summary>
    public GameTick CreationTick { get; internal set; } = GameTick.Zero;

    /// <summary>
    /// The last tick <see cref="EntityManager.Dirty"/> was called on this comp.
    /// </summary>
    public GameTick LastModifiedTick { get; internal set; } = GameTick.Zero;

    /// <summary>
    /// AUTO GENERATED: Writes the state of this component that goes to the clients.
    /// </summary>
    public virtual void WriteNetState(NetBuffer buffer, EntityManager entMan)
    {
    }

    /// <summary>
    /// AUTO GENERATED: Reads what <see cref="WriteNetState"/> wrote. <see cref="EntityUid"/> members net entity are resolved through
    /// <paramref name="entMan"/>.
    /// </summary>
    public virtual void ReadNetState(NetBuffer buffer, EntityManager entMan)
    {
    }

    /// <summary>
    /// MANUAL: for components whose data doesn't fit per-field replication (e.g. a tilemap's chunks). Returns a
    /// <c>[Serializable]</c> <see cref="IComponentState"/> with whatever changed since <paramref name="fromTick"/>,
    /// or null if nothing did. <para/>
    /// Overriding this (together with <see cref="HandleNetState"/>) make the NetworkedComponent
    /// component OUT of the auto generated.
    /// <see cref="WriteNetState"/>/<see cref="ReadNetState"/> (DO NOT USE <see cref="NetFieldAttribute"/>)
    /// </summary>
    public virtual IComponentState? GetNetState(GameTick fromTick) => null;

    /// <summary>
    /// MANUAL: applies a state built by <see cref="GetNetState"/>.
    /// </summary>
    public virtual void HandleNetState(IComponentState state)
    {
    }

    internal void RemoveComponent()
    {
        MainThread.AssertMainThread();

        Deleted = true;
        State = CompState.Removing;
        IoCManager.Resolve<EntityManager>().CompsPendingRemove.Add(this);
    }

    public enum CompState
    {
        /// <summary>
        /// Component is currently being added to the comp tree.
        /// </summary>
        Adding,
        /// <summary>
        /// It is all done, the component is running normally.
        /// </summary>
        Running,
        /// <summary>
        /// The component is marked to be deleted in the next frame.
        /// </summary>
        Removing,
    }
}

public abstract class ComponentEvent : EntityEvent
{
    public Component Component;
}

/// <summary>
/// <strong>ONLY USE THIS IF YOU KNOW WHAT U ARE DOING</strong><para/>
/// Called when the component are in the process of being added. No comp features are avaible (like Owner). <para/>
/// For normal usage see <seealso cref="CompAddedEvent"/>
/// </summary>
public sealed class CompInitEvent : ComponentEvent
{
}

/// <summary>
/// Called when the component is added to the entity and is ready to go.
/// </summary>
public sealed class CompAddedEvent : ComponentEvent
{
}

/// <summary>
/// Called right before the component removal.
/// </summary>
public sealed class CompRemovedEvent : ComponentEvent
{
}
