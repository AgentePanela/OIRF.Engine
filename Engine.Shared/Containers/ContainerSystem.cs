using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.Physics;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Containers;

/// <summary>
/// Controls everything about containers and contained entities.
/// </summary>
public sealed partial class ContainerSystem : EntitySystem
{
    [Dependency] private readonly TransformSystem _transform = default!;

    // item > the container that lists it
    private readonly Dictionary<EntityUid, BaseContainer> _containedIn = new();

    // items whose owner (or its container) has not arrived on this client yet
    private readonly HashSet<EntityUid> _pending = new();

    private readonly List<EntityUid> _buffer = new();

    public override void Init()
    {
        base.Init();

        SubscribeEvent<ContainerComponent, CompAddedEvent>(OnContainerAdded);
        SubscribeEvent<ContainerComponent, CompRemovedEvent>(OnContainerRemoved);
        SubscribeEvent<ContainerComponent, ComponentStateAppliedEvent>(OnContainerState);

        SubscribeEvent<ContainedComponent, CompAddedEvent>(OnContainedAdded);
        SubscribeEvent<ContainedComponent, CompRemovedEvent>(OnContainedRemoved);
        SubscribeEvent<ContainedComponent, ComponentStateAppliedEvent>(OnContainedState);
        SubscribeEvent<ContainedComponent, EntitySceneChangedEvent>(OnContainedSceneChanged);
    }

    #region Authority

    private bool IsConnectedClient => _internalNetMan.IsClient && _internalNetMan.MySession is not null;

    private bool IsLocallyOwned(EntityUid uid)
        => !IsConnectedClient || !_entManager.TryGetNetEntity(uid, out _);

    /// <summary>
    /// Whether this side may move <paramref name="item"/> in or out of a container of <paramref name="owner"/>.
    /// </summary>
    public bool CanMutate(EntityUid owner, EntityUid item)
    {
        if (IsLocallyOwned(owner) && IsLocallyOwned(item))
            return true;

        Log.Debug($"Refused to change the container of {item} in {owner}: a connected client can only move its own entities.");
        return false;
    }

    #endregion

    #region API

    /// <summary>
    /// Gets the container <paramref name="id"/> of <paramref name="owner"/>, declaring it (and adding a
    /// <see cref="ContainerComponent"/>) if missing.
    /// </summary>
    public T EnsureContainer<T>(EntityUid owner, string id) where T : BaseContainer
    {
        var kind = typeof(T) == typeof(ContainerSlot) ? ContainerKind.Slot : ContainerKind.Container;
        var comp = EnsureComp<ContainerComponent>(owner);

        if (comp.Containers.TryGetValue(id, out var declared))
        {
            if (declared != kind)
                throw new InvalidOperationException($"Container '{id}' of {owner} is a {declared}, not a {kind}.");

            if (!comp.Instances.ContainsKey(id))
                SyncInstances(owner, comp);

            return (T)comp.Instances[id];
        }

        if (!IsLocallyOwned(owner))
            Log.Warn($"Container '{id}' declared on {owner} by a client Fixing in next state.");

        comp.Containers[id] = kind;
        Dirty(owner, comp);
        SyncInstances(owner, comp);

        return (T)comp.Instances[id];
    }

    /// <summary>
    /// Empties (to the owner position) and drops the container <paramref name="id"/> of <paramref name="owner"/>.
    /// </summary>
    public bool RemoveContainer(EntityUid owner, string id)
    {
        if (!TryComp<ContainerComponent>(owner, out var comp) || !comp.Containers.Remove(id))
            return false;

        if (!IsLocallyOwned(owner))
            Log.Warn($"Container '{id}' removed from {owner} by a client. Fixing in next state");

        Dirty(owner, comp);
        SyncInstances(owner, comp);
        return true;
    }

    public bool TryGetContainer(EntityUid owner, string id, [NotNullWhen(true)] out BaseContainer? container)
    {
        container = null;
        return TryGetInstances(owner, out var instances) && instances.TryGetValue(id, out container);
    }

    public IEnumerable<BaseContainer> GetAllContainers(EntityUid owner)
        => TryGetInstances(owner, out var instances) ? instances.Values : Array.Empty<BaseContainer>();

    // the instances are built on CompAddedEvent, which runs at the end of the update the entity was spawned in
    private bool TryGetInstances(EntityUid owner, [NotNullWhen(true)] out Dictionary<string, BaseContainer>? instances)
    {
        instances = null;
        if (!TryComp<ContainerComponent>(owner, out var comp))
            return false;

        if (comp.Instances.Count != comp.Containers.Count)
            SyncInstances(owner, comp);

        instances = comp.Instances;
        return true;
    }

    /// <summary>
    /// The container listing <paramref name="item"/>, if any.
    /// </summary>
    public bool TryGetContainingContainer(EntityUid item, [NotNullWhen(true)] out BaseContainer? container)
        => _containedIn.TryGetValue(item, out container);

    public bool IsEntityInContainer(EntityUid item) => HasComp<ContainedComponent>(item);

    /// <summary>
    /// The outermost owner holding <paramref name="item"/> (a backpack wearer, not the backpack).
    /// </summary>
    public bool TryGetOuterOwner(EntityUid item, out EntityUid root)
    {
        root = item;

        while (_containedIn.TryGetValue(root, out var container))
            root = container.Owner;

        return root != item;
    }

    /// <summary>
    /// Whether <paramref name="item"/> could go into <paramref name="container"/> right now, attempt events included.
    /// </summary>
    public bool CanInsert(EntityUid item, BaseContainer container, bool force = false)
    {
        var owner = container.Owner;

        if (item == owner || !HasEntity(item, out var itemEnt) || itemEnt.Deleting)
            return false;

        if (!HasEntity(owner, out var ownerEnt) || ownerEnt.Deleting || !HasComp<TransformComponent>(item))
            return false;

        if (!TryGetInstances(owner, out var instances) || !instances.TryGetValue(container.Id, out var live)
            || !ReferenceEquals(live, container))
            return false;

        if (container.Contains(item) || !container.HasRoom)
            return false;

        // the owner sitting somewhere inside the item
        if (_transform.WouldCycle(item, owner))
            return false;

        if (!CanMutate(owner, item))
            return false;

        if (force)
            return true;

        var ownerEv = new ContainerIsInsertingAttemptEvent { Container = container, Entity = item };
        RaiseEvent(owner, ownerEv);
        if (ownerEv.Cancelled)
            return false;

        var itemEv = new ContainerGettingInsertedAttemptEvent { Container = container, Entity = item };
        RaiseEvent(item, itemEv);
        return !itemEv.Cancelled;
    }

    /// <summary>
    /// Puts <paramref name="item"/> into <paramref name="container"/>, taking it out of any other first.
    /// </summary>
    /// <param name="force">Skip the attempt events, and take it out of its current container without asking.</param>
    public bool Insert(EntityUid item, BaseContainer container, bool force = false)
    {
        if (!CanInsert(item, container, force))
            return false;

        if (_containedIn.TryGetValue(item, out var old) && !force && !CanRemove(item, old))
            return false;

        var owner = container.Owner;

        // moving between containers keeps the component
        Unlink(item);

        SetScene(item, GetScene(owner));

        var xform = Transform(item)!;
        _transform.SetParent(item, owner, keepWorld: false, xform);
        xform.LocalPosition = Vector2.Zero;
        xform.LocalAngle = 0f;

        if (TryComp<PhysicsComponent>(item, out var physics))
            physics.Velocity = Vector2.Zero;

        // remove then Insert in the same tick finds the old component still waiting to go
        if (TryComp<ContainedComponent>(item, out var contained) && contained.Deleted)
            _entManager.CancelRemoval(contained);

        contained = EnsureComp<ContainedComponent>(item);
        contained.ContainerOwner = owner;
        contained.ContainerId = container.Id;
        contained.Order = container.NextOrder++;

        Link(item, owner, container.Id);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="item"/> could come out of <paramref name="container"/>.
    /// </summary>
    public bool CanRemove(EntityUid item, BaseContainer container, bool force = false)
    {
        if (!container.Contains(item) || !CanMutate(container.Owner, item))
            return false;

        if (force)
            return true;

        var ownerEv = new ContainerIsRemovingAttemptEvent { Container = container, Entity = item };
        RaiseEvent(container.Owner, ownerEv);
        if (ownerEv.Cancelled)
            return false;

        var itemEv = new ContainerGettingRemovedAttemptEvent { Container = container, Entity = item };
        RaiseEvent(item, itemEv);
        return !itemEv.Cancelled;
    }

    /// <summary>
    /// Takes <paramref name="item"/> out of its container and puts it in the world.
    /// </summary>
    public bool Remove(EntityUid item, bool force = false, Vector2? dropAt = null)
    {
        if (!_containedIn.TryGetValue(item, out var container) || !CanRemove(item, container, force))
            return false;

        var where = dropAt ?? Transform(container.Owner)?.Position ?? Vector2.Zero;

        Unlink(item);
        RemComp<ContainedComponent>(item);

        if (TryTransform(item, out var xform))
        {
            _transform.SetParent(item, null, keepWorld: false, xform);
            xform.Position = where;
            xform.Angle = 0f;
        }

        return true;
    }

    /// <summary>
    /// Takes everything out of <paramref name="container"/>.
    /// </summary>
    /// <returns>False if anything stayed in.</returns>
    public bool EmptyContainer(BaseContainer container, bool force = false, Vector2? dropAt = null)
    {
        var items = new List<EntityUid>(container.Entities);
        var all = true;

        foreach (var item in items)
            all &= Remove(item, force, dropAt);

        return all;
    }

    /// <summary>
    /// Deletes everything in <paramref name="container"/>. (only happends after container system updates)
    /// </summary>
    public void CleanContainer(BaseContainer container)
    {
        if (!CanMutate(container.Owner, container.Owner))
            return;

        foreach (var item in new List<EntityUid>(container.Entities))
            DeleteEntity(item);
    }

    #endregion
}
