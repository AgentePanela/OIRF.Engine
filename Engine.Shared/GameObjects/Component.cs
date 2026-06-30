using Engine.Shared.IoC;

namespace Engine.Shared.GameObjects;

public class Component
{
    public EntityUid Owner { get; set; } = EntityUid.Empty;
    public bool Deleted { get; internal set; } = false;
    /// <summary>
    /// Represents the current state of the component
    /// </summary>
    public CompState State { get; internal set; } = CompState.Adding;

    internal void RemoveComponent()
    {
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
