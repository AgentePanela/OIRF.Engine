# Resources

The ORIF Engine loads all textures through a **texture atlas** system. Images are automatically packed into atlas pages at startup, improving draw-call efficiency. `IAssetManager` is the primary API for working with game assets.

---

## IAssetManager

`IAssetManager` (accessible via `GameClient.Assets`) is the public API for sprite and texture access.

### Getting Sprites

```csharp
// Get a Sprite2D ready for rendering
if (GameClient.Assets.GetSprite("player/idle", out Sprite2D sprite))
{
    _renderer.Submit(sprite, position);
}

// Check if a sprite key exists without loading
bool exists = GameClient.Assets.HasSprite("player/idle");
```

### Getting Atlas Textures (lower level)

```csharp
// Retrieve the raw atlas sprite and its page
if (GameClient.Assets.GetTexture("player/idle", out AtlasSprite atlasSpr, out AtlasPage atlasPage))
{
    // atlasPage.Texture  → Texture2D
    // atlasSpr.Region    → Rectangle within the atlas texture
}
```

### Dynamic Sprites

You can add textures to the atlas at runtime (e.g., for procedural content):

```csharp
// Wrap a loaded Texture2D in a TextureRect
var texRect = new TextureRect { Texture = myTexture };

// Add to the atlas
Sprite2D sprite = GameClient.Assets.AddSprite(texRect, key: "dynamic/mySprite");

// Remove a dynamic sprite
GameClient.Assets.RemoveSprite("dynamic/mySprite");
```

### Atlas Pages

```csharp
// Get all loaded atlas pages (for debugging/custom rendering)
List<AtlasPage> pages = GameClient.Assets.GetAllAtlasses();
```

---

## File Structure

Place all textures inside the `Resources/Textures/` folder. The engine scans this directory recursively at startup and packs every image into the atlas.

```
Resources/
  Textures/
    player/
      idle.png
      walk.png
    enemies/
      goblin.png
    tiles/
      grass.png
      stone.png
```

The **asset key** is the relative path from `Resources/Textures/`, without the extension, using forward slashes:

| File | Key |
|------|-----|
| `Resources/Textures/player/idle.png` | `player/idle` |
| `Resources/Textures/tiles/grass.png` | `tiles/grass` |

---

## ResPath

`ResPath` is a lightweight wrapper around a directory path relative to `Resources/`. It is used internally by the engine to locate prototype and locale folders.

```csharp
var path = new ResPath("Prototypes");

// Get all matching subdirectories
string[] folders = path.GetFolders();
```

You generally do not need to use `ResPath` directly in game code — it is used internally by the prototype and localization managers.

---

## Atlas Size Configuration

Set the texture atlas page size via `EntryPointOptions.TextureAtlasSize`:

```csharp
new EntryPointOptions
{
    TextureAtlasSize = AtlasSize.Size2048, // default
}
```

| Value | Atlas page size |
|-------|----------------|
| `Size512` | 512 × 512 |
| `Size1024` | 1024 × 1024 |
| `Size2048` | 2048 × 2048 (default) |
| `Size4096` | 4096 × 4096 |

If a single texture is too large to fit on a standard atlas page, the engine automatically creates a dedicated atlas page for it (controlled by `EntryPointOptions.CreateDedicatedAtlas`).

---

## Hot Reloading

The asset manager supports hot-reloading textures from disk during development. When a texture file changes, the atlas is updated automatically without restarting the game.

This is enabled by default in debug builds.
