using Engine.Shared.GameObjects;

namespace Engine.Shared.Containers;

public abstract class ContainerAttemptEvent : CancellableEntityEvent
{
    public BaseContainer Container = default!;

    /// <summary>
    /// The entity going in or out.
    /// </summary>
    public EntityUid Entity;
}

public abstract class ContainerChangedEvent : EntityEvent
{
    public BaseContainer Container = default!;

    /// <summary>
    /// The entity that went in or out.
    /// </summary>
    public EntityUid Entity;
}

/// <summary>
/// Raised on the container owner before an entity goes in. Cancel to refuse.
/// </summary>
public sealed class ContainerIsInsertingAttemptEvent : ContainerAttemptEvent
{
}

/// <summary>
/// Raised on the entity before it goes into a container. Cancel to refuse.
/// </summary>
public sealed class ContainerGettingInsertedAttemptEvent : ContainerAttemptEvent
{
}

/// <summary>
/// Raised on the container owner before an entity comes out. Cancel to refuse.
/// </summary>
public sealed class ContainerIsRemovingAttemptEvent : ContainerAttemptEvent
{
}

/// <summary>
/// Raised on the entity before it comes out of a container. Cancel to refuse.
/// </summary>
public sealed class ContainerGettingRemovedAttemptEvent : ContainerAttemptEvent
{
}

/// <summary>
/// Raised on the container owner after an entity went in.
/// </summary>
public sealed class EntInsertedIntoContainerEvent : ContainerChangedEvent
{
}

/// <summary>
/// Raised on the entity after it went into a container.
/// </summary>
public sealed class EntGotInsertedIntoContainerEvent : ContainerChangedEvent
{
}

/// <summary>
/// Raised on the container owner after an entity came out, deletion included.
/// </summary>
public sealed class EntRemovedFromContainerEvent : ContainerChangedEvent
{
}

/// <summary>
/// Raised on the entity after it came out of a container, deletion included.
/// </summary>
public sealed class EntGotRemovedFromContainerEvent : ContainerChangedEvent
{
}
