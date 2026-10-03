using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;

namespace Engine.Shared.Containers;

public sealed partial class ContainerSystem
{
    private void OnContainerAdded(EntityUid uid, ContainerComponent comp, CompAddedEvent args)
    {
        // a clone gets the source dictionary by reference
        comp.Containers = new Dictionary<string, ContainerKind>(comp.Containers);
        SyncInstances(uid, comp);
    }

    private void OnContainerState(EntityUid uid, ContainerComponent comp, ComponentStateAppliedEvent args)
    {
        SyncInstances(uid, comp);
        LinkPending();
    }

    private void OnContainerRemoved(EntityUid uid, ContainerComponent comp, CompRemovedEvent args)
    {
        // a deleting owner takes its contents with it (TransformSystem deletes children)
        if (GetEntity(uid)?.Deleting is not false)
            return;

        foreach (var container in comp.Instances.Values)
            DropContainer(container);

        comp.Instances.Clear();
    }

    private void OnContainedAdded(EntityUid uid, ContainedComponent comp, CompAddedEvent args)
    {
        // on a client this fires before the first state, which links it instead
        if (comp.Deleted || !IsLocallyOwned(uid) || _containedIn.ContainsKey(uid))
            return;

        // not put there by Insert: a clone of a contained entity, or added by hand
        Log.Debug($"{uid} got a Contained component outside of a container, removing it.");
        RemComp<ContainedComponent>(uid);
        _transform.Detach(uid);
    }

    private void OnContainedState(EntityUid uid, ContainedComponent comp, ComponentStateAppliedEvent args)
        => Link(uid, comp.ContainerOwner, comp.ContainerId);

    private void OnContainedRemoved(EntityUid uid, ContainedComponent comp, CompRemovedEvent args)
    {
        _pending.Remove(uid);
        Unlink(uid);
    }

    private void OnContainedSceneChanged(EntityUid uid, ContainedComponent comp, EntitySceneChangedEvent args)
    {
        if (!_containedIn.TryGetValue(uid, out var container) || !IsLocallyOwned(uid))
            return;

        // its owner moving carries it along, so only a lone move gets here with a different scene
        if (ReferenceEquals(GetScene(container.Owner), args.New))
            return;

        Remove(uid, force: true, dropAt: Transform(uid)?.Position);
    }
}
