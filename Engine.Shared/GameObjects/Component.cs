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
    /// AUTO GENERATED: How many <see cref="NetFieldAttribute"/> members this component has. The generated state
    /// writes them ordered by name, and that order is what every field index here refers to.
    /// </summary>
    public virtual int NetFieldCount => 0;

    /// <summary>
    /// AUTO GENERATED: The tick a single field last changed on, used to pick what goes in a delta. Falls back to the
    /// whole component's <see cref="LastModifiedTick"/> when the component does not track its fields apart - see
    /// <see cref="AutoDirtyAttribute"/>.
    /// </summary>
    public virtual GameTick GetFieldTick(int index) => LastModifiedTick;

    /// <summary>
    /// AUTO GENERATED: for the dirty machinery, use <see cref="EntityManager.Dirty(Component, int)"/> instead.
    /// </summary>
    public virtual void SetFieldTick(int index, GameTick tick)
    {
    }

    /// <summary>
    /// Called by the setter the generator writes for an <see cref="AutoDirtyAttribute"/> field.
    /// </summary>
    protected void DirtyField(int index) => EntityManager.Instance?.Dirty(this, index);

    /// <summary>
    /// Marks the whole component as changed. For a component that mutates a collection of its own.
    /// </summary>
    protected void Dirty() => EntityManager.Instance?.Dirty(this);

    /// <summary>
    /// AUTO GENERATED: Writes the state of this component that goes to the clients. Only the fields that changed
    /// after <paramref name="fromTick"/> are written, behind a mask - <see cref="GameTick.Zero"/> writes them all.
    /// </summary>
    public virtual void WriteNetState(NetBuffer buffer, EntityManager entMan, GameTick fromTick)
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

    internal IComponentState? LastServerState;

    /// <summary>
    /// AUTO GENERATED: copies the live values into the shadow of what the server last said, right after a state is
    /// applied. A component that builds its own state keeps <see cref="LastServerState"/> instead.
    /// </summary>
    public virtual void SaveServerState()
    {
    }

    /// <summary>
    /// Puts the last value the server sent back into the component.
    /// AUTO GENERATED for a component with <see cref="NetFieldAttribute"/> members.
    /// </summary>
    public virtual void RestoreServerState()
    {
        if (LastServerState is not null)
            HandleNetState(LastServerState);
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
