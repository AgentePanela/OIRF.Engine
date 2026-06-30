# ECS — Entity Component System

ORIF uses an **Entity-Component-System (ECS)** architecture as the backbone of all game logic.  
All game objects are entities. Behaviour is attached via components. Logic is processed by systems.

---

## Core Concepts

| Concept | Type | Description |
|---------|------|-------------|
| **Entity** | `EntityUid` | A lightweight numeric ID representing a game object |
| **Component** | `Component` | Pure data attached to an entity |
| **System** | `EntitySystem` | Logic that queries and processes components every frame |
| **Event Bus** | `EventBus` | Decoupled messaging between systems and components |

---

## EntityUid

`EntityUid` is an immutable integer ID. It is the only handle you need to refer to an entity.

```csharp
EntityUid uid = _entManager.CreateEmptyEntity("Player");

// Check for equality
if (uid == EntityUid.Empty) { /* invalid entity */ }

// uid.Id gives the raw int value
int raw = uid.Id;
```

`EntityUid.Empty` is the sentinel for "no entity" / invalid.

---

## Components

All components inherit from `Component`.

```csharp
// Minimal custom component
public sealed class HealthComponent : Component
{
    public int MaxHealth { get; set; } = 100;
    public int CurrentHealth { get; set; } = 100;
}
```

### Registration

Components must be registered with a name so the prototype system can reference them:

```csharp
[RegisterComponent("Health")]
public sealed class HealthComponent : Component { ... }
```

### Component State

Every component has a `CompState` that tracks its lifecycle:

| State | Description |
|-------|-------------|
| `Adding` | Being added — `Owner` is not yet available. Do not use component features here. |
| `Running` | Fully active and available. |
| `Removing` | Marked for removal on the next frame. |

---

## EntityManager — Entity API

`EntityManager` (accessible via `GameClient.EntityManager` or injected via `[Dependency]`) manages all entity and component lifetime.

### Creating Entities

```csharp
// Create a blank entity with an optional name
EntityUid uid = _entManager.CreateEmptyEntity("MyEntity");

// Create from a prototype (see Prototypes docs)
EntityUid uid = _entManager.CreateEntity(new ProtoId<EntityPrototype>("MyProto"));

// Create from a prototype at a world position
EntityUid uid = _entManager.CreateEntity(new ProtoId<EntityPrototype>("MyProto"), new Vector2(100, 200));
```

### Querying Entities

```csharp
// Check if entity exists in the current scene
if (_entManager.HasEntity(uid, out Entity? ent)) { ... }

// Get the Entity object (returns null if not found)
Entity? ent = _entManager.GetEntity(uid);

// Total entity count
int count = _entManager.GetEntityCount();
```

### Deleting Entities

```csharp
// Marks entity (and all its components) for removal on the next frame
_entManager.DeleteEntity(uid);
```

---

## EntityManager — Component API

### Adding & Ensuring

```csharp
// Add — throws if the entity already has this component
HealthComponent hp = _entManager.AddComp<HealthComponent>(uid);

// EnsureComp — returns existing or creates a new one; never throws
HealthComponent hp = _entManager.EnsureComp<HealthComponent>(uid);
```

### Reading

```csharp
// Returns null if the entity does not have the component
HealthComponent? hp = _entManager.GetComp<HealthComponent>(uid);

// Pattern-matching style — preferred
if (_entManager.TryComp<HealthComponent>(uid, out var hp))
{
    hp.CurrentHealth -= 10;
}

// Boolean check only
bool hasHp = _entManager.HasComp<HealthComponent>(uid);
```

### Removing

```csharp
// Marks the component for removal on the next frame
_entManager.RemComp<HealthComponent>(uid);

// Get all components on an entity
List<Component>? comps = _entManager.GetEntityComps(uid);
```

### Querying Multiple Entities

Use `Query<T>()` to iterate all entities that have a given set of components:

```csharp
// One component
foreach (var (uid, hp) in _entManager.Query<HealthComponent>())
{
    Console.WriteLine($"{uid} has {hp.CurrentHealth} HP");
}

// Two components — iterates the smaller pool first (optimised)
foreach (var (uid, hp, transform) in _entManager.Query<HealthComponent, TransformComponent>())
{
    // ...
}

// Three components
foreach (var (uid, hp, transform, physics) in
    _entManager.Query<HealthComponent, TransformComponent, PhysicsComponent>())
{
    // ...
}
```

---

## EntitySystem

`EntitySystem` is the base class for all game-logic systems. Override the virtual methods you need:

```csharp
public sealed class HealthSystem : EntitySystem
{
    public override void Init()
    {
        // Subscribe to events and set up state here.
        SubscribeEvent<HealthComponent, CompAddedEvent>(OnHealthAdded);
    }

    public override void Update(float dt)
    {
        // Called every frame (delta time in seconds).
        foreach (var (uid, hp) in GetEntitiesWithComp<HealthComponent>())
        {
            if (hp.CurrentHealth <= 0)
                DeleteEntity(uid);
        }
    }

    public override void Draw(float dt)
    {
        // Only rendering logic here.
    }

    public override void OnShutdown()
    {
        // Clean up before the game closes.
    }

    private void OnHealthAdded(EntityUid uid, HealthComponent hp, CompAddedEvent ev)
    {
        hp.CurrentHealth = hp.MaxHealth;
    }
}
```

### EntitySystem Shorthand Methods

All `EntityManager` and `EventBus` methods are available as protected/public shorthands on `EntitySystem`:

**Components:**
```csharp
AddComp<T>(uid)         // Add a component
EnsureComp<T>(uid)      // Add or get existing
GetComp<T>(uid)         // Get or null
TryComp<T>(uid, out T?) // Try get
HasComp<T>(uid)         // Check existence
RemComp<T>(uid)         // Remove
GetEntityComps(uid)     // All components on entity
```

**Queries:**
```csharp
GetEntitiesWithComp<T>()
GetEntitiesWithComp<T1, T2>()
GetEntitiesWithComp<T1, T2, T3>()
```

**Entities:**
```csharp
CreateEmptyEntity(name?)
GetEntity(uid)
HasEntity(uid, out ent)
DeleteEntity(uid)
```

**Transform shorthand:**
```csharp
TransformComponent? t = Transform(uid);
bool ok = TryTransform(uid, out var t);
```

---

## EventBus

The `EventBus` is ORIF's messaging backbone. It lets systems communicate without tight coupling.

All events must inherit from `EntityEvent`:

```csharp
public sealed class PlayerDiedEvent : EntityEvent
{
    public int Score { get; init; }
}
```

`EntityEvent` has two built-in fields:
- `Uid` — set automatically by `RaiseEvent(uid, ev)`.
- `Handled` — set to `true` to stop event propagation.

### Global Events

A **global event** fires regardless of any specific entity:

```csharp
// Subscribe (usually in Init())
SubscribeEvent<PlayerDiedEvent>(OnPlayerDied);

// Raise
RaiseEvent(new PlayerDiedEvent { Score = 500 });

// Handler
void OnPlayerDied(PlayerDiedEvent ev) { ... }
```

### Entity + Component Events

An **entity event** with component binding fires **only if the entity has the required component**:

```csharp
// Subscribe — fires only when the entity has HealthComponent
SubscribeEvent<HealthComponent, PlayerDiedEvent>(OnPlayerDied);

// Raise for a specific entity
RaiseEvent(uid, new PlayerDiedEvent { Score = 500 });

// Handler receives uid, the component, and the event
void OnPlayerDied(EntityUid uid, HealthComponent hp, PlayerDiedEvent ev) { ... }
```

### Built-in Lifecycle Events

| Event | When it fires |
|-------|--------------|
| `EntityInitEvent` | Immediately when entity is created (internal only) |
| `EntityAddedEvent` | After the entity is fully added to the scene |
| `EntityRemovedEvent` | Right before the entity is removed from the scene |
| `CompInitEvent` | When a component is being initialised — `Owner` not yet available |
| `CompAddedEvent` | When a component is fully added and running |
| `CompRemovedEvent` | Right before a component is removed |

```csharp
// Example: react when a component becomes active
SubscribeEvent<HealthComponent, CompAddedEvent>(OnHealthAdded);

void OnHealthAdded(EntityUid uid, HealthComponent hp, CompAddedEvent ev)
{
    hp.CurrentHealth = hp.MaxHealth;
}
```

---

## TransformComponent

`TransformComponent` is the position component. Most entities that exist in the game world will have one:

```csharp
var transform = EnsureComp<TransformComponent>(uid);
transform.Position = new Vector2(100, 200);
```

| Property | Type | Description |
|----------|------|-------------|
| `Position` | `Vector2` | World-space position of the entity |
