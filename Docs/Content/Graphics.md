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

`Label2D` draws text with optional shadow and outline. It can be driven three ways: a raw `SpriteFontBase`, a managed `FontKey`, or a high-level `TextStyle` (see [Fonts](Fonts.md) for how those resolve to an actual font).

```csharp
// Managed font-key path
var label = new Label2D(FontKey.UiBody, "Hello, World!")
{
    Color = Color.White,
    Layer = 10,
    ShadowEnabled = true,
    ShadowColor   = Color.Black,
    ShadowOffset  = new Vector2(1, 1),
    OutlineEnabled   = true,
    OutlineColor     = Color.Black,
    OutlineThickness = 1,
};

_renderer.Submit(label, position);

// High-level style path, font/size/color/effects all come from the style
var title = new Label2D(TextStyle.Title, "Game Over");
_renderer.Submit(title, position);
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

## Shapes

Beyond sprites and text, `RenderManager` can draw vector shapes directly: circles, rectangles, lines, polygons, and more, without needing a texture. These are backed by [Apos.Shapes](https://github.com/Apos-Games/Apos.Shapes) and queued through the same submit pipeline as everything else, so they respect `Layer`/`Depth` and participate in lighting like any other renderable.

```csharp
_renderer.DrawCircle(center: new Vector2(400, 300), radius: 32, fillColor: Color.Orange);

_renderer.DrawRect(new Rectangle(100, 100, 200, 80),
    fillColor: Color.CornflowerBlue,
    borderColor: Color.White,
    thickness: 2,
    rounded: new CornerRadii(8));

_renderer.DrawLine(from: a, to: b, radius: 2, fillColor: Color.Red);
```

| Method | Shape |
|--------|-------|
| `DrawRect(rect, fill?, border?, thickness, rounded, rotation, unshaded)` | Axis-aligned or rounded rectangle |
| `DrawCircle(center, radius, fill?, border?, thickness, rotation, unshaded)` | Circle |
| `DrawEllipse(center, radiusX, radiusY, fill?, border?, thickness, rotation, unshaded)` | Ellipse |
| `DrawLine(from, to, radius, fill?, border?, thickness, unshaded)` | Line segment with thickness |
| `DrawHexagon(center, radius, fill?, border?, thickness, rounded, rotation, unshaded)` | Regular hexagon |
| `DrawEquilateralTriangle(center, radius, fill?, border?, thickness, rounded, rotation, unshaded)` | Equilateral triangle |
| `DrawTriangle(a, b, c, fill?, border?, thickness, rounded, unshaded)` | Arbitrary 3-point triangle |
| `DrawArc(center, angle1, angle2, radius1, radius2, fill?, border?, thickness, unshaded)` | Arc/pie slice between two angles and radii |
| `DrawRing(center, angle1, angle2, radius1, radius2, fill?, border?, thickness, unshaded)` | Ring segment (arc with inner and outer radius) |
| `DrawPolygon(worldVerts, border?, thickness, unshaded)` | Closed polygon outline, built from `DrawLine` segments |

Each shape's `fillColor`/`borderColor` parameters are a `Gradient` (from `Apos.Shapes`). A plain `Color` is implicitly converted to a solid-color gradient, so you can pass `Color.Red` directly wherever a `Gradient` is expected, or construct an actual multi-stop gradient for fades.

Every shape struct implements `IShapeRenderable` (which extends `IRenderable`) and carries its own `Unshaded` flag:

```csharp
_renderer.DrawCircle(center, radius, fillColor: Color.Yellow, unshaded: true);
```

This is a separate concept from the `Unshaded` **shader** used by sprites (see [Unshaded Sprites in Lighting.md](Lighting.md#unshaded-sprites)). Shapes can't hold a custom `Effect`, so `Unshaded` is just a bool that tells the renderer to skip lighting for that shape. As with unshaded sprites, `Layer`/`Depth` are still respected normally against everything else in the scene.

For immediate-mode drawing outside the queue (rare, prefer the `Draw*` methods above), `GameClient.ShapeBatch` exposes the underlying `Apos.Shapes` batch directly, e.g. `_renderer.FillRectImmediate(rect, color)`.

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

The adapter is initialized with the values from `ClientOptions.Width/Height`.

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

### Z-order within a layer

Every `IRenderable` also has a `Depth` (float). Within a single `Layer`, renderables
are drawn in ascending `Depth` order — higher `Depth` draws later, i.e. on top. This
is the same two-level model as Construct 3's Layers + per-instance Z-order: `Layer`
picks the coarse bucket, `Depth` orders things within it. Renderables that don't set
`Depth` default to `0` and keep drawing in submit order relative to each other, so
existing content is unaffected.

```csharp
sprite.Layer = 5;
sprite.Depth = 1; // drawn on top of other Layer-5 sprites with lower/default Depth
```

On `SpriteComponent`, set `depth:` from a prototype, or use the convenience helpers
on `SpriteSystem` for the common "always on top/bottom" case:

```csharp
_spriteSystem.BringToFront(comp); // comp.Depth = float.MaxValue
_spriteSystem.SendToBack(comp);   // comp.Depth = float.MinValue
```
