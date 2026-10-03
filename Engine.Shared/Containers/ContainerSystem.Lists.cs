using System.Collections.Generic;
using Engine.Shared.GameObjects;

namespace Engine.Shared.Containers;

public sealed partial class ContainerSystem
{
    // the declaration (replicated) drives the runtime containers
    private void SyncInstances(EntityUid owner, ContainerComponent comp)
    {
        List<string>? stale = null;
        foreach (var (id, container) in comp.Instances)
        {
            if (!comp.Containers.TryGetValue(id, out var kind) || kind != container.Kind)
                (stale ??= new List<string>()).Add(id);
        }

        if (stale is not null)
        {
            foreach (var id in stale)
            {
                DropContainer(comp.Instances[id]);
                comp.Instances.Remove(id);
            }
        }

        foreach (var (id, kind) in comp.Containers)
        {
            if (!comp.Instances.ContainsKey(id))
                comp.Instances[id] = BaseContainer.Create(kind, id, owner);
        }
    }

    // a container going away: owned contents fall at the owner, replicated ones wait for their next state
    private void DropContainer(BaseContainer container)
    {
        _buffer.Clear();
        _buffer.AddRange(container.Entities);

        foreach (var item in _buffer)
        {
            if (IsLocallyOwned(item))
            {
                Remove(item, force: true);
                continue;
            }

            Unlink(item);
            _pending.Add(item);
        }
    }

    private void LinkPending()
    {
        if (_pending.Count == 0)
            return;

        _buffer.Clear();
        _buffer.AddRange(_pending);

        foreach (var item in _buffer)
        {
            if (TryComp<ContainedComponent>(item, out var comp))
                Link(item, comp.ContainerOwner, comp.ContainerId);
            else
                _pending.Remove(item);
        }
    }

    private void Link(EntityUid item, EntityUid owner, string id)
    {
        if (_containedIn.TryGetValue(item, out var current))
        {
            if (current.Owner == owner && current.Id == id)
            {
                // same container, maybe a new Order: move it without telling anyone it left
                current.Entities.Remove(item);
                InsertOrdered(current, item);
                return;
            }

            Unlink(item);
        }

        if (!TryGetInstances(owner, out var instances) || !instances.TryGetValue(id, out var container))
        {
            _pending.Add(item);
            return;
        }

        _pending.Remove(item);
        InsertOrdered(container, item);
        _containedIn[item] = container;

        RaiseEvent(owner, new EntInsertedIntoContainerEvent { Container = container, Entity = item });
        RaiseEvent(item, new EntGotInsertedIntoContainerEvent { Container = container, Entity = item });
    }

    private void InsertOrdered(BaseContainer container, EntityUid item)
    {
        var order = OrderOf(item);
        var entities = container.Entities;
        int lo = 0, hi = entities.Count;

        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (OrderOf(entities[mid]) <= order)
                lo = mid + 1;
            else
                hi = mid;
        }

        entities.Insert(lo, item);
    }

    private int OrderOf(EntityUid item)
        => TryComp<ContainedComponent>(item, out var comp) ? comp.Order : int.MaxValue;

    private void Unlink(EntityUid item)
    {
        if (!_containedIn.Remove(item, out var container))
            return;

        container.Entities.Remove(item);

        // the owner may be the one being deleted...
        RaiseEvent(container.Owner, new EntRemovedFromContainerEvent { Container = container, Entity = item });
        RaiseEvent(item, new EntGotRemovedFromContainerEvent { Container = container, Entity = item });
    }
}
