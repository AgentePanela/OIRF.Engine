using System.Collections.Generic;

namespace Engine.Shared.Containers;

/// <summary>
/// The kinds of container a <see cref="ContainerComponent"/> can declare.
/// </summary>
public enum ContainerKind : byte
{
    /// <inheritdoc cref="Containers.Container"/>
    Container,

    /// <inheritdoc cref="ContainerSlot"/>
    Slot,
}

public abstract class BaseContainer
{
    public string Id { get; }

    /// <summary>
    /// The entity that holds this container.
    /// </summary>
    public EntityUid Owner { get; }

    public abstract ContainerKind Kind { get; }

    // a slot uses a list too
    internal readonly List<EntityUid> Entities = new();

    // where changes are made: the next ContainedComponent.Order to hand out
    internal int NextOrder;

    public IReadOnlyList<EntityUid> ContainedEntities => Entities;

    public int Count => Entities.Count;

    protected BaseContainer(string id, EntityUid owner)
    {
        Id = id;
        Owner = owner;
    }

    public bool Contains(EntityUid uid) => Entities.Contains(uid);

    /// <summary>
    /// Whether this container has room for one more entity.
    /// </summary>
    public abstract bool HasRoom { get; }

    internal static BaseContainer Create(ContainerKind kind, string id, EntityUid owner) => kind switch
    {
        ContainerKind.Slot => new ContainerSlot(id, owner),
        _ => new Container(id, owner),
    };

    public override string ToString() => $"{Owner}/{Id}";
}

/// <summary>
/// Holds any number of entities, in insertion order.
/// </summary>
public sealed class Container(string id, EntityUid owner) : BaseContainer(id, owner)
{
    public override ContainerKind Kind => ContainerKind.Container;

    public override bool HasRoom => true;
}

/// <summary>
/// Holds one entity at most.
/// </summary>
public sealed class ContainerSlot(string id, EntityUid owner) : BaseContainer(id, owner)
{
    public override ContainerKind Kind => ContainerKind.Slot;

    public override bool HasRoom => Entities.Count == 0;

    /// <summary>
    /// The entity in the slot, if any.
    /// </summary>
    public EntityUid? ContainedEntity => Entities.Count > 0 ? Entities[0] : null;
}
