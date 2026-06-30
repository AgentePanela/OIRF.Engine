# Graphics

The ORIF Engine renders all 2D content through `RenderManager`. It wraps MonoGame's `SpriteBatch` with a layered render queue, camera transforms, and viewport management.

---

## RenderManager

`RenderManager` (accessible via `GameClient.Renderer`) is the central rendering API.

### Render Queue

Rather than drawing directly to the screen, you **submit** renderables to the queue. The queue is sorted by layer, then flushed in one pass after all systems have run.

```csharp
// Submit a Sprite2D renderable
_renderer.Submit(mySprite, position);

// Submit with a custom shader (Effect)
_renderer.Submit(mySprite, position, shader: myEffect);
```

All `IRenderable` types have a `Layer` property. Lower layer values are drawn first (behind).

### Raw Drawing (inside Begin/End)

For immediate rendering (e.g., inside a custom draw call), you can use the lower-level methods:

```csharp
_renderer.Begin();

_renderer.DrawSprite(sprite2D, position);
_renderer.DrawString(label2D, position);
_renderer.DrawTexture(textureRect, position);

_renderer.End();
```

> **Note:** Prefer using the queue via `Submit()`. Only use `Begin()/End()` when you specifically need immediate rendering outside the queue.

### Screen-to-World Conversion

```csharp
Vector2 worldPos = _renderer.ScreenToWorld(screenPosition);
```

---

## Renderable Types

### Sprite2D

`Sprite2D` is a struct that holds all data needed to draw a sprite from the texture atlas.

```csharp
// Load a sprite from the asset manager
if (GameClient.Assets.GetSprite("player/idle", out var sprite))
{
    _renderer.Submit(sprite, position);
}
```

| Property | Type | Description |
|----------|------|-------------|
| `Key` | `string` | Asset key in the atlas |
| `Color` | `Color` | Tint colour (default `Color.White`) |
| `Rotation` | `float` | Rotation in radians |
| `Origin` | `Vector2` | Pivot point for rotation/scale |
| `Scale` | `Vector2` | Scale multiplier |
| `Depth` | `float` | Depth within the same layer |
| `Offset` | `Vector2` | Sub-texture offset within the atlas region |
| `Layer` | `int` | Render layer |

### Label2D

`Label2D` draws text with optional shadow and outline.

```csharp
var label = new Label2D
{
    String   = "Hello, World!",
    FontKey  = "fonts/myFont",
    Color    = Color.White,
    Scale    = Vector2.One,
    Layer    = 10,
    // Shadow
    Shadow        = true,
    ShadowColor   = Color.Black,
    ShadowOffset  = new Vector2(1, 1),
    // Outline
    Outline          = true,
    OutlineColor     = Color.Black,
    OutlineThickness = 1f,
};

_renderer.Submit(label, position);
```

### TextureRect

`TextureRect` wraps a raw `Texture2D` (not from the atlas) for direct drawing.

```csharp
var texRect = new TextureRect
{
    Texture = myTexture2D,
    Color   = Color.White,
    Layer   = 5,
};

_renderer.Submit(texRect, position);
```

---

## SpriteComponent

`SpriteComponent` is an ECS component that stores a sprite key. Combined with a sprite system, it lets entities be rendered automatically.

```csharp
[RegisterComponent("Sprite")]
public sealed class SpriteComponent : Component
{
    public string? SpriteKey { get; set; }
    public int Layer { get; set; } = 0;
    // ... other sprite properties
}
```

Assign it from a prototype:

```yaml
components:
  - type: Sprite
    spriteKey: player/idle
    layer: 5
```

---

## Camera2D

`Camera2D` (accessible via `GameClient.Camera`) controls the 2D view.

```csharp
// Move the camera
GameClient.Camera.Position = new Vector2(500, 300);

// Zoom
GameClient.Camera.Zoom = 1.5f;

// Get the view matrix (applied automatically by RenderManager)
Matrix view = GameClient.Camera.GetViewMatrix();

// Convert screen coordinates to world coordinates
Vector2 world = GameClient.Camera.ScreenToWorld(screenPos);
```

The camera transform is applied automatically when `RenderManager.DrawQueue()` runs.

---

## ViewportAdapter

`ViewportAdapter` (accessible via `GameClient.Viewport`) manages virtual resolution scaling. It ensures the game renders at a consistent virtual size even when the window is resized.

The adapter is initialized with the values from `EntryPointOptions.Width/Height`.

---

## Render Layers

Render layers are plain integers. The engine sorts all submitted renderables by layer before drawing:

| Convention | Layer value | Used for |
|------------|-------------|---------|
| Background | `0` | Tilemaps, floors |
| World objects | `5` | Most entities |
| Effects | `8` | Particles, VFX |
| UI world | `10` | World-space UI |

These are conventions — you can use any integer values.

Set the layer on the renderable:

```csharp
sprite.Layer = 5;
_renderer.Submit(sprite, position);
```
