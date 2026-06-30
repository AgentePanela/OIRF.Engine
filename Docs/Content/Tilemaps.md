# Tilemaps

ORIF includes a chunk-based tilemap system for building large tile worlds efficiently. Tiles are defined via prototypes and the tilemap is managed through `TilemapSystem`.

---

## Overview

The tilemap system is built from three main pieces:

| Type | Role |
|------|------|
| `TilemapComponent` | Data component attached to an entity — holds all chunks |
| `TilemapChunk` | A fixed-size grid of tile prototype IDs |
| `TilePrototype` | YAML definition for a tile (sprite, solidity) |
| `TilemapSystem` | Renders tiles and provides the manipulation API |

---

## TilePrototype

Define tiles in YAML:

```yaml
- type: tile
  id: Grass
  sprite: tiles/grass
  solid: false

- type: tile
  id: Stone
  sprite: tiles/stone
  solid: true
```

| Field | Required | Description |
|-------|----------|-------------|
| `type` | ✅ | Always `tile` |
| `id` | ✅ | Unique tile identifier |
| `sprite` | ✅ | Asset key for the tile texture |
| `solid` | ❌ | Whether this tile blocks movement (default `false`) |

---

## TilemapComponent

`TilemapComponent` is attached to a regular entity. It defines the visual and chunk layout settings.

```csharp
var uid      = CreateEmptyEntity("World");
var tilemap  = EnsureComp<TilemapComponent>(uid);
var transform = EnsureComp<TransformComponent>(uid);
transform.Position = Vector2.Zero;

tilemap.TileSize    = 128;   // pixels per tile
tilemap.ChunkSize   = 16;    // tiles per chunk side
tilemap.Layer       = 0;     // render layer
tilemap.TileBlending = true; // enable terrain blending
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TileSize` | `int` | `128` | Width and height of each tile in pixels |
| `ChunkSize` | `int` | `16` | Number of tiles per chunk side (chunk is ChunkSize × ChunkSize) |
| `Layer` | `int` | `0` | Render layer for all tiles |
| `SamplerState` | `SamplerState?` | `null` | Override sampler (null = global default) |
| `TileBlending` | `bool` | `true` | Enable terrain blending between different tile types |

---

## TilemapChunk

A chunk is a fixed `ChunkSize × ChunkSize` grid of optional tile IDs. Each chunk is identified by its chunk coordinates `(ChunkX, ChunkY)`.

```csharp
// Chunk coordinates (0,0) is the top-left chunk
var chunk = new TilemapChunk(cx: 0, cy: 0, size: tilemap.ChunkSize);
```

Tile positions are `ProtoId<TilePrototype>?` — `null` means the tile slot is empty.

---

## TilemapSystem API

Inject `TilemapSystem` via `[Dependency]` in your entity system to manipulate tilemaps.

### Managing Chunks

```csharp
// Add a chunk to the tilemap
_tilemapSystem.AddChunk(tilemap, new TilemapChunk(0, 0, tilemap.ChunkSize));

// Remove a chunk
_tilemapSystem.RemoveChunk(tilemap, cx: 0, cy: 0);

// Get a chunk (returns null if not found)
TilemapChunk? chunk = _tilemapSystem.GetChunk(tilemap, cx: 0, cy: 0);

// Remove all chunks
_tilemapSystem.Clear(tilemap);
```

### Reading and Writing Tiles

Tile positions use **world tile coordinates** — not chunk-local coordinates.

```csharp
// Set a tile at world tile position (5, 3)
_tilemapSystem.SetTile(tilemap, worldTileX: 5, worldTileY: 3,
    tile: new ProtoId<TilePrototype>("Grass"));

// Erase a tile
_tilemapSystem.SetTile(tilemap, 5, 3, tile: null);

// Get the tile at a world tile position (returns null if empty or chunk missing)
ProtoId<TilePrototype>? tileId = _tilemapSystem.GetTile(tilemap, 5, 3);

// Check if a tile is solid
bool solid = _tilemapSystem.IsTileSolid(tilemap, worldTileX: 5, worldTileY: 3);
```

### Coordinate Conversion

```csharp
// World position → tile coordinate
Point tile = _tilemapSystem.WorldToTile(tilemap, worldPos: new Vector2(640, 384));
// tile.X = world pixel X / TileSize

// Tile coordinate → world position
Vector2 worldPos = _tilemapSystem.TileToWorld(tilemap, transform, tileX: 5, tileY: 3);

// Tile coordinate → chunk coordinate
Point chunk = _tilemapSystem.TileToChunk(tilemap, tileX: 5, tileY: 3);

// Chunk coordinate → world position (top-left of the chunk)
Vector2 chunkWorld = _tilemapSystem.ChunkToWorld(tilemap, transform, cx: 0, cy: 0);

// Tile global → tile local (within its chunk)
Point local = _tilemapSystem.TileToLocal(tilemap, tileX: 5, tileY: 3);
```

### Solid Tile Queries

Use this to feed solid tile data to your own movement or collision logic:

```csharp
var solidRects = new List<Rectangle>();

_tilemapSystem.GetSolidTilesInArea(
    tilemap,
    tilemapTransform: transform,
    area: new Rectangle(playerX - 64, playerY - 64, 128, 128),
    results: solidRects
);

foreach (var rect in solidRects)
{
    // rect is in world-pixel space
}
```

---

## Example — Generating a Simple Map

```csharp
public override void OnSceneStart()
{
    var mapUid   = CreateEmptyEntity("Map");
    var tilemap  = EnsureComp<TilemapComponent>(mapUid);
    var transform = EnsureComp<TransformComponent>(mapUid);

    tilemap.TileSize  = 64;
    tilemap.ChunkSize = 16;

    // Add a single chunk at (0,0)
    var chunk = new TilemapChunk(0, 0, tilemap.ChunkSize);
    _tilemapSystem.AddChunk(tilemap, chunk);

    // Fill it with grass
    for (int x = 0; x < tilemap.ChunkSize; x++)
    for (int y = 0; y < tilemap.ChunkSize; y++)
        _tilemapSystem.SetTile(tilemap, x, y, "Grass");

    // Place some stone tiles
    _tilemapSystem.SetTile(tilemap, 5, 5, "Stone");
    _tilemapSystem.SetTile(tilemap, 5, 6, "Stone");
}
```

---

## Dirty Flag & Rendering

When you call `SetTile`, the affected chunk is automatically marked `Dirty = true`. The `TilemapSystem.Draw()` method detects this and rebuilds the chunk's cached renderable on the next frame — no manual cache invalidation is needed.

Only chunks inside the camera's viewport bounds are rendered each frame (frustum culling).
