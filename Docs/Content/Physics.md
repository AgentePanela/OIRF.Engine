# Physics

ORIF provides a lightweight 2D physics system built on top of its ECS. It handles collision detection, event dispatch, and basic velocity-driven movement.

---

## Overview

The physics pipeline is composed of two systems:

| System | Responsibility |
|--------|---------------|
| `CollisionSystem` | Broad-phase (spatial hash) + narrow-phase overlap detection, fires `CollisionStartEvent`/`CollisionEndEvent` |
| `PhysicsSystem` | Applies velocity, friction, and collision response (MTV push) |

---

## PhysicsComponent

Add `PhysicsComponent` to any entity that should move or be pushed by collisions.

```csharp
var physics = EnsureComp<PhysicsComponent>(uid);
physics.Velocity = new Vector2(100, 0); // move right at 100 units/sec
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Velocity` | `Vector2` | `Zero` | Current velocity in world units per second |
| `Static` | `bool` | `false` | If `true`, this entity is never moved by collision resolution |
| `Friction` | `float` | `0.3` | Linear drag applied to `Velocity` every frame (0 = no drag, 1 = instant stop) |
| `Mass` | `float` | `1.0` | Reserved for future impulse physics |
| `Restitution` | `float` | `0.0` | Reserved (bounciness) |

> **Tip:** To move an entity, write to `physics.Velocity`. Do **not** write to both `Velocity` and `transform.Position` in the same frame — the physics system already applies velocity to the transform.

---

## CollisionComponent

`CollisionComponent` defines the collision shape(s) of an entity. An entity can have multiple named **fixtures**, each with its own shape and layer configuration.

```csharp
var collision = EnsureComp<CollisionComponent>(uid);

// Add a box fixture
collision.AddFixture("body", new CollisionFixture
{
    Shape  = new BoxShape { Size = new Vector2(32, 32) },
    Layers = new HashSet<string> { "player" },
    Masks  = new HashSet<string> { "enemy", "wall" },
    Hard   = true,
});
```

### CollisionComponent API

| Method | Description |
|--------|-------------|
| `AddFixture(id, fixture)` | Add a named fixture |
| `GetFixture(id)` | Get a fixture by name, or `null` |
| `RemoveFixture(id)` | Remove a fixture |
| `Active` | Enable/disable all collision for this entity |

### CollisionFixture

| Property | Type | Description |
|----------|------|-------------|
| `Shape` | `CollisionShape` | The hitbox shape |
| `Layers` | `HashSet<string>` | Layers this fixture **belongs to** |
| `Masks` | `HashSet<string>` | Layers this fixture **detects** (collides with) |
| `Hard` | `bool` | If `true`, physically blocks movement. If `false`, fires events only (trigger zone) |

---

## Collision Shapes

All shapes derive from `CollisionShape` and have an `Offset` property (local offset from the entity's transform position).

### BoxShape

An axis-aligned rectangle.

```csharp
new BoxShape
{
    Size   = new Vector2(32, 48),      // width, height in pixels
    Offset = new Vector2(0, -8),       // shift up by 8 pixels
}
```

### CircleShape

A circle defined by a radius.

```csharp
new CircleShape
{
    Radius = 16f,
    Offset = Vector2.Zero,
}
```

### PolygonShape

A convex polygon defined by local-space vertices.

```csharp
new PolygonShape
{
    Vertices = new[]
    {
        new Vector2(0, -16),
        new Vector2(16, 16),
        new Vector2(-16, 16),
    }
}
```

---

## Collision Layers & Masks

The layer/mask system controls **which fixtures can detect each other**:

- A collision occurs between two fixtures when **at least one side** can see the other:
  - Fixture A's `Masks` overlaps with Fixture B's `Layers`, **or**
  - Fixture B's `Masks` overlaps with Fixture A's `Layers`.

```csharp
// Player fixture
Layers = { "player" }
Masks  = { "enemy", "wall" }

// Enemy fixture
Layers = { "enemy" }
Masks  = { "player" }

// → player↔enemy collision fires for both sides
```

---

## Collision Events

When two fixtures begin or stop overlapping, the engine fires events on both entities.

### CollisionStartEvent

Fired on the **first frame** two fixtures overlap.

```csharp
SubscribeEvent<CollisionComponent, CollisionStartEvent>(OnCollisionStart);

void OnCollisionStart(EntityUid uid, CollisionComponent comp, CollisionStartEvent ev)
{
    // The other entity
    EntityUid other = ev.Other;

    // Which fixtures touched
    string selfFixture  = ev.SelfFixtureId;
    string otherFixture = ev.OtherFixtureId;

    // Penetration vector that pushes this entity away from the other (zero for soft collisions)
    Vector2 push = ev.PenetrationVector;

    // True if both fixtures are Hard (physical collision)
    bool isHard = ev.IsHard;
}
```

### CollisionEndEvent

Fired when two fixtures **stop overlapping**.

```csharp
SubscribeEvent<CollisionComponent, CollisionEndEvent>(OnCollisionEnd);

void OnCollisionEnd(EntityUid uid, CollisionComponent comp, CollisionEndEvent ev)
{
    EntityUid other = ev.Other;
    string selfFixture  = ev.SelfFixtureId;
    string otherFixture = ev.OtherFixtureId;
}
```

---

## CollisionSystem Queries

`CollisionSystem` (inject via `[Dependency]`) provides several runtime queries:

### Overlap Checks

```csharp
// Are entity A and B currently touching?
bool touching = _collision.IsColliding(uidA, uidB);

// Is entity touching anything at all?
bool any = _collision.IsCollidingWithAnything(uid);

// Is entity colliding on a specific fixture?
bool onBody = _collision.IsCollidingOnFixture(uid, "body");

// Get all entities currently colliding with uid
var others = new List<EntityUid>();
_collision.GetColliding(uid, others);
```

### Position Queries

```csharp
// Get the entity whose fixtures contain a world-space point
EntityUid hit = _collision.GetEntityAtPosition(worldPos);

// With a layer mask filter
EntityUid hit = _collision.GetEntityAtPosition(
    worldPos,
    mask: new HashSet<string> { "enemy" }
);

// Try-version (returns false if nothing found)
if (_collision.TryGetEntityAtPosition(worldPos, out var uid)) { ... }

// Get all entities at a position
List<EntityUid> all = _collision.GetEntitiesAtPosition(worldPos);
```

### Raycasting

```csharp
// Closest hit
if (_collision.Raycast(origin, direction, maxDistance, out RaycastHit hit))
{
    EntityUid entity = hit.Entity;
    string fixtureId = hit.FixtureId;
    float  distance  = hit.Distance;
    Vector2 point    = hit.Point;
}

// All hits, sorted by distance (for piercing/line-of-sight)
List<RaycastHit> hits = _collision.RaycastAll(origin, direction, maxDistance);

// With layer mask
List<RaycastHit> hits = _collision.RaycastAll(
    origin, direction, maxDistance,
    mask: new HashSet<string> { "wall" }
);
```

---

## Static Entities

Marking an entity as `Static = true` prevents collision resolution from ever moving it. The full MTV push is transferred to the dynamic body instead.

```csharp
// Wall entity — never moves
var physics = EnsureComp<PhysicsComponent>(wallUid);
physics.Static = true;
```

Use this for walls, floors, platforms, and any immovable geometry.

---

## Trigger Zones

Set `CollisionFixture.Hard = false` to create a trigger (sensor) zone that fires events without physically blocking movement:

```csharp
collision.AddFixture("trigger", new CollisionFixture
{
    Shape  = new BoxShape { Size = new Vector2(64, 64) },
    Layers = { "trigger" },
    Masks  = { "player" },
    Hard   = false,   // ← no physical resolution
});
```
