# Containers

Containers let an entity hold other entities: a mob's hands, a backpack, a gun's magazine slot, a locker. A contained
entity still exists (it keeps all its components and state), it just leaves the world: it is not drawn, does not
collide, does not move on its own and is not found by spatial queries. It rides along with whoever holds it.

---

## Overview

| Class | Role |
|-------|------|
| `ContainerComponent` | On the owner. Declares its containers, by id |
| `BaseContainer` | One named container: `Container` (any number of entities) or `ContainerSlot` (one at most) |
| `ContainedComponent` | On the contained entity. Says which owner and container hold it |
| `ContainerSystem` | The API: declare containers, insert, remove, query |

---

## What "contained" means

Inserting an entity:

1. moves it to the owner's scene,
2. parents it to the owner at local zero (`LocalPosition = 0`, `LocalAngle = 0`), so its world position *is* the
   owner's - see [Parenting](Ecs.md#parenting),
3. zeroes its physics velocity,
4. gives it a `ContainedComponent`.

Every world system skips entities that have a `ContainedComponent`:

| Skipped by | Effect |
|------------|--------|
| `SpriteSystem` | not drawn |
| `LightingSystem` | its lights and occluders are off |
| `CollisionSystem`, `TileCollisionSystem` | no collisions, not hit by point queries or raycasts |
| `PhysicsSystem` | does not move on its own |
| `TransformSystem` | no `MoveEvent`; not returned by `TryGetEntityAtWorld`/`GetEntitiesInArea` |
| Debug draws (collision, occluders, net ids) | not drawn |

Audio is **not** skipped: a radio in a backpack keeps playing, at the position of whoever carries it.

`CollisionComponent.Active` and `Transform.Visible` are left alone, so taking the entity out restores it exactly as it
was.

---

## Declaring containers

In YAML, on the owner, id to kind (`Container` or `Slot`):

```yaml
- type: entity
  id: Backpack
  components:
  - type: Transform
  - type: Container
    containers:
      storage: Container
      pocket: Slot
```

Or from code, where a system that needs a container makes sure it exists:

```csharp
[Dependency] private readonly ContainerSystem _containers = default!;

var hand = _containers.EnsureContainer<ContainerSlot>(uid, "hand");
var bag  = _containers.EnsureContainer<Container>(uid, "storage");
```

`EnsureContainer` adds the `ContainerComponent` if missing and throws if the id is already declared with the other
kind.

---

## API

### Inserting and removing

```csharp
// put item into a container; takes it out of any other container first
bool ok = _containers.Insert(item, bag);

// take it out, at the owner position or wherever you say
_containers.Remove(item);
_containers.Remove(item, dropAt: new Vector2(100, 50));

// everything out / everything deleted
_containers.EmptyContainer(bag);
_containers.CleanContainer(bag);

// ask first without doing it (raises the attempt events)
if (_containers.CanInsert(item, bag)) { ... }
```

`force: true` on `Insert`/`Remove`/`EmptyContainer` skips the attempt events.

`Insert` refuses when:

- the item is the owner itself, or the owner is somewhere inside the item,
- either entity does not exist or is being deleted, or the item has no transform,
- the container is a full `ContainerSlot`, or already lists the item,
- an attempt event is cancelled,
- this side is not allowed to change it (see [Who can change a container](#who-can-change-a-container)).

### Querying

```csharp
_containers.TryGetContainer(owner, "storage", out var container);
_containers.GetAllContainers(owner);

_containers.IsEntityInContainer(item);                  // has a ContainedComponent
_containers.TryGetContainingContainer(item, out var c); // which one
_containers.TryGetOuterOwner(item, out var root);        // the backpack's wearer, not the backpack

foreach (var uid in bag.ContainedEntities) { ... }
var held = handSlot.ContainedEntity;                     // EntityUid? on a slot
```

Read containers freely, but change them only through `ContainerSystem`, or nothing replicates and no events fire.

---

## Events

Attempt events derive from `CancellableEntityEvent` (see [Cancellable Events](Ecs.md#cancellable-events)) and are
raised only where the change is made. Result events are raised on every side that sees the change, including a client
receiving it from the server.

| Event | Raised on | Cancellable |
|-------|-----------|-------------|
| `ContainerIsInsertingAttemptEvent` | owner | yes |
| `ContainerGettingInsertedAttemptEvent` | item | yes |
| `ContainerIsRemovingAttemptEvent` | owner | yes |
| `ContainerGettingRemovedAttemptEvent` | item | yes |
| `EntInsertedIntoContainerEvent` | owner | no |
| `EntGotInsertedIntoContainerEvent` | item | no |
| `EntRemovedFromContainerEvent` | owner | no |
| `EntGotRemovedFromContainerEvent` | item | no |

All of them carry `Container` and `Entity` (the item).

```csharp
SubscribeEvent<GunComponent, ContainerIsInsertingAttemptEvent>(OnMagazineInsertAttempt);

void OnMagazineInsertAttempt(EntityUid uid, GunComponent gun, ContainerIsInsertingAttemptEvent ev)
{
    if (ev.Container.Id == "magazine" && !HasComp<MagazineComponent>(ev.Entity))
        ev.Cancel(stopPropagation: false);
}
```

The removed events also fire when a contained entity is deleted.

---

## Lifecycle

| What happens | Result |
|--------------|--------|
| Owner deleted | contents deleted with it (call `EmptyContainer` first to drop them) |
| Contained entity deleted | leaves the list, removed events fire |
| `ContainerComponent` removed from a living owner | contents dropped at the owner |
| Container id removed (`RemoveContainer`) | its contents dropped at the owner |
| Owner changes scene | contents go with it |
| Contained entity moved to another scene on its own | taken out, where it is |
| Container inside a container | keeps its contents; they follow the chain of owners |
| Contained entity cloned | the clone is not contained (stripped one update later) |

---

## Who can change a container

There is no client prediction: changes happen where entities are owned.

| Where | Allowed |
|-------|---------|
| Server | everything |
| Client not connected (single-player, editor, test scenes) | everything |
| Connected client, owner **and** item local to the client | yes |
| Connected client, anything replicated from the server | no - `Insert`/`Remove` return `false` |

`ContainerSystem.CanMutate(owner, item)` answers this.

---

## Networking

- `ContainerComponent` replicates only the declaration (ids and kinds).
- Each contained entity replicates its `ContainedComponent` (owner, container id and `Order`). The client rebuilds the
  lists from those, so an item that arrives before its owner (or the other way around) lands in the right list once
  both are there. Its `ContainedComponent` arrives with it, so it is hidden from the first frame either way.
- Lists are kept sorted by `ContainedComponent.Order`, which insertion stamps from a per-container counter that only
  grows. So a `Container` has the same order on every side, whatever order its entities arrived in, and a changed
  `Order` moves the entity in the list (without removed/inserted events).
- Whoever sees the owner receives the contents (same chunk, same PVS rules as any entity). Contents count against
  `net.pvs-enter-budget` like any entity and may arrive a few ticks after their owner.

---

## Debug commands

Entities are given as `n<id>` (the net id drawn by the net ids overlay) or as a local uid. On a connected client the
command runs on the server unless both entities are local.

| Command | Does |
|---------|------|
| `container_insert <owner> <item> [id=debug] [slot]` | inserts, declaring the container if missing (`slot` makes it a `Slot`) |
| `container_remove <item>` | takes it out at the owner position |
| `container_empty <owner> [id]` | empties one container, or all |
| `container_list <owner>` | lists the containers as this side sees them |
