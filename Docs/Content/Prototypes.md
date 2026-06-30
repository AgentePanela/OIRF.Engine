# Prototypes

The ORIF Engine uses a **YAML-based prototype system** to define game content as data. Prototypes describe the starting state of entities (and other data types) without requiring hard-coded C# for every object variant.

---

## What is a Prototype?

A **prototype** is a YAML document that the engine loads at startup. Prototypes let you:

- Define entity templates with pre-configured components.
- Share and reuse data through inheritance.
- Tweak game content without recompiling.

---

## File Location

All prototype files must be placed inside the `Resources/Prototypes/` folder (or subdirectories). The engine scans every `.yaml` file found there recursively.

```
Resources/
  Prototypes/
    entities/
      player.yaml
      enemies.yaml
    tiles/
      grass.yaml
```

---

## Entity Prototypes

An **entity prototype** defines a template entity with a set of components and their data.

### Minimal Example

```yaml
- type: entity
  id: MyPlayer
  name: Player
  components:
    - type: Transform
      x: 0
      y: 0
    - type: Health
      maxHealth: 100
```

### Full Field Reference

| Field | Required | Description |
|-------|----------|-------------|
| `type` | ✅ | Always `entity` for entity prototypes |
| `id` | ✅ | Unique string identifier |
| `name` | ❌ | Human-readable display name |
| `abstract` | ❌ | If `true`, cannot be spawned — only used as a parent |
| `parent` | ❌ | One or more parent prototype IDs to inherit from |
| `components` | ❌ | List of component data entries |

### Component Entry

Each component entry inside `components` must have a `type` matching a registered `[RegisterComponent]` name. Additional fields are mapped to `[DataField]` properties on the component.

```yaml
components:
  - type: Health         # Maps to [RegisterComponent("Health")]
    maxHealth: 200       # Maps to [DataField("maxHealth")]
    currentHealth: 150
```

---

## Prototype Inheritance

Prototypes can inherit from other prototypes using `parent`. Child fields override parent fields; component entries with matching types are merged field-by-field.

```yaml
# Abstract base — cannot be spawned directly
- type: entity
  id: BaseEnemy
  abstract: true
  components:
    - type: Health
      maxHealth: 50
    - type: Physics
      friction: 0.5

# Concrete child — inherits from BaseEnemy
- type: entity
  id: Goblin
  parent: BaseEnemy
  name: Goblin
  components:
    - type: Health
      maxHealth: 80      # Overrides BaseEnemy's 50
    # Physics is inherited unchanged
```

Multiple parents are supported:

```yaml
- type: entity
  id: BossGoblin
  parent: [ BaseEnemy, BossBase ]
```

---

## Spawning Entities from Prototypes

Use `EntityManager.CreateEntity` to spawn a prototype at runtime:

```csharp
// Spawn at origin
EntityUid uid = _entManager.CreateEntity(new ProtoId<EntityPrototype>("Goblin"));

// Spawn at a world position (adds TransformComponent automatically)
EntityUid uid = _entManager.CreateEntity(
    new ProtoId<EntityPrototype>("Goblin"),
    new Vector2(400, 300)
);

// Spawn with a name override
EntityUid uid = _entManager.CreateEntity(
    new ProtoId<EntityPrototype>("Goblin"),
    "Big Goblin"
);
```

> Spawning an `abstract` prototype throws an exception.

---

## ProtoId\<T\>

`ProtoId<T>` is a typed string wrapper that makes prototype references safe and explicit:

```csharp
var id = new ProtoId<EntityPrototype>("Goblin");

// ProtoId.Value gives the raw string
string raw = id.Value; // "Goblin"

// Implicit conversion from string
ProtoId<EntityPrototype> id = "Goblin";
```

---

## IPrototypeManager

`IPrototypeManager` (accessible via `GameClient.Prototypes`) provides runtime access to all loaded prototypes.

```csharp
// Get a prototype by ID (throws if not found)
EntityPrototype proto = _proto.Index(new ProtoId<EntityPrototype>("Goblin"));

// Try to get a prototype
if (_proto.TryIndex(new ProtoId<EntityPrototype>("Goblin"), out var proto))
{
    Console.WriteLine(proto.Name);
}

// Check if a prototype exists
bool exists = _proto.HasIndex(new ProtoId<EntityPrototype>("Goblin"));

// Iterate all prototypes of a type
foreach (var proto in _proto.EnumerateAll<EntityPrototype>())
{
    Console.WriteLine(proto.ID);
}

// Get all as a read-only dictionary keyed by ID
IReadOnlyDictionary<string, EntityPrototype> all = _proto.GetAll<EntityPrototype>();

// Count prototypes
int count = _proto.Count<EntityPrototype>();
int total  = _proto.Count();
```

---

## Custom Prototype Types

You can define your own prototype types for game-specific data (items, tiles, input maps, etc.).

```csharp
[Prototype("item")]
public sealed class ItemPrototype : IPrototype
{
    [DataField("type", required: true)]
    public string Type { get; private set; } = "item";

    [DataField("id", required: true)]
    public string ID { get; private set; } = default!;

    [DataField("displayName")]
    public string DisplayName { get; private set; } = string.Empty;

    [DataField("stackSize")]
    public int StackSize { get; private set; } = 1;
}
```

Then in YAML:

```yaml
- type: item
  id: Sword
  displayName: Iron Sword
  stackSize: 1
```

### DataField Attribute

`[DataField]` maps a C# property or field to a YAML key.

| Parameter | Description |
|-----------|-------------|
| `name` | YAML key name. Defaults to the member name if omitted. |
| `required` | If `true`, an error is thrown when the key is missing in YAML. |

---

## Built-in Prototype Types

| YAML type | C# class | Description |
|-----------|----------|-------------|
| `entity` | `EntityPrototype` | Entity templates with components |
| `tag` | `TagPrototype` | Tag definitions |
| `inputMap` | `InputMapPrototype` | Input action bindings |
| `tile` | `TilePrototype` | Tile definitions for tilemaps |
| `randomWeights` | `RandomWeightsPrototype` | Weighted random selection tables |
