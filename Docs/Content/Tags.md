# Tags

The tag system provides a lightweight way to label entities with named flags. Tags are checked efficiently and can be used to categorise entities without adding heavyweight components.

---

## Overview

| Class | Role |
|-------|------|
| `TagPrototype` | YAML definition that registers a valid tag ID |
| `TagComponent` | Component that stores the set of tags on an entity |
| `TagSystem` | Provides the query and mutation API |

---

## Defining Tags in YAML

All tag IDs must be declared as `TagPrototype` entries before use. In debug builds, attaching an undeclared tag throws an exception.

```yaml
- type: tag
  id: Player

- type: tag
  id: Enemy

- type: tag
  id: Interactable
```

---

## Attaching Tags via Prototype

Add a `Tag` component entry to an entity prototype:

```yaml
- type: entity
  id: PlayerEntity
  components:
    - type: Tag
      tags:
        - Player
        - Interactable
```

---

## TagSystem API

Inject `TagSystem` via `[Dependency]` in your system:

```csharp
[Dependency] private readonly TagSystem _tags = default!;
```

### Checking Tags

```csharp
// Does the entity have a specific tag?
bool isPlayer = _tags.HasTag(uid, "Player");

// Does the entity have ALL of the given tags?
bool isBoss = _tags.HasTags(uid, "Enemy", "Boss");
```

### Adding and Removing Tags

```csharp
// Add a tag (creates TagComponent if missing)
_tags.AddTag(uid, "Stunned");

// Remove a tag
_tags.RemoveTag(uid, "Stunned");
```

### Querying Entities by Tag

```csharp
// All entities with a specific tag
foreach (EntityUid uid in _tags.GetEntitiesWithTag("Enemy"))
{
    // ...
}

// All entities that have ALL given tags
foreach (EntityUid uid in _tags.GetEntitiesWithTags("Enemy", "Stunned"))
{
    // ...
}
```

---

## TagComponent

`TagComponent` stores the set of tags on a single entity.

```csharp
if (_entManager.TryComp<TagComponent>(uid, out var tagComp))
{
    foreach (var tag in tagComp.Tags)
        Console.WriteLine(tag);
}
```

The `Tags` property is a `HashSet<string>` of prototype IDs.

---

## ProtoId\<TagPrototype\>

Like all prototype references, tag IDs are wrapped in `ProtoId<TagPrototype>` for type safety. In most cases, an implicit conversion from `string` is available:

```csharp
// Implicit conversion — both lines are equivalent
bool a = _tags.HasTag(uid, "Player");
bool b = _tags.HasTag(uid, new ProtoId<TagPrototype>("Player"));
```
