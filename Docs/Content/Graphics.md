# Graphics

The ORIF Engine renders all 2D content through `IRenderManager`. It wraps MonoGame's `SpriteBatch` with a layered render queue, camera transforms, and viewports.

---

## RenderManager

`IRenderManager` (injected with `[Dependency]`, or `GameClient.Renderer`) is the central rendering API.

## Viewports

The world is never drawn straight onto the window. Each `RenderViewport` has its own camera and its own render target; the engine draws every enabled viewport into its target and then puts the main one on the window.

```csharp
RenderViewport main = _renderer.MainViewport;   // what the player sees, its camera is the Camera2D from IoC

// another view of the world, drawn every frame before the main one
RenderViewport cctv = _renderer.CreateViewport("Cctv", new Vector2(320, 180), renderScale: 1f);
cctv.Camera.LookAt(somewhere);
Texture2D frame = cctv.ColorTarget;              // last frame drawn, null until the first one

_renderer.RemoveViewport(cctv);
```

| Member | Description |
|---|---|
| `Size` | The size the camera centers on, in world units at zoom 1 |
| `RenderScale` | Target pixels per world unit at zoom 1 |
| `TargetSize` | Target size in pixels. Defaults to `Size * RenderScale`; a different one keeps the view centered and crops or shows more |
| `ClearColor` | What the target is cleared to (the scene background when null) |
| `Enabled` | A disabled viewport is not drawn |
| `Resize(size, renderScale, targetSize)` | Changes the three above; the target is recreated on the next draw |

`IRenderManager.MainViewportScaling` says how the main viewport follows the window: `Scaled` (the game resolution fit to the window, the default), `Native` (one world unit per window pixel) or `Manual` (the engine leaves it alone). With `PresentMainViewport = false` the main viewport is still drawn into its target but not put on the window, and the engine UI is skipped - that is how the editor shows the world inside one of its own panels.

### Draw systems run once per viewport

`IEntityDrawSystem.Draw(in DrawContext ctx)` is called for each viewport being drawn. Anything that depends on the view has to come from the context, not from the main camera:

```csharp
public override void Draw(in DrawContext ctx)
{
    foreach (var (uid, xform) in GetEntitiesWithComp<TransformComponent>())
    {
        if (!ctx.Camera.IsOnScreen(xform.Position))   // the camera of the viewport being drawn
            continue;

        _renderer.DrawCircle(xform.Position, 2f, Color.Red);
    }
}
```

`DrawContext` carries `DeltaTime`, `Viewport`, `Camera` and `WorldBounds`. What a scene submits from its own `Draw(float dt)` goes to the main viewport.

### Render Queue

Rather than drawing directly to the screen, you **submit** to the queue. The queue is sorted by layer, then depth, then the order things were submitted in, and drawn in one pass after all systems have run.

```csharp
// Submit a Sprite2D
_renderer.Submit(mySprite, position);

// Submit with a shader (see Shaders.md)
_renderer.Submit(mySprite, position, _shaders.GetInstance("Grayscale"));
```

Lower layer values are drawn first (behind).

A `Sprite2D` goes into the queue as plain data. Anything else that needs its own drawing code implements `IRenderable`: its `Draw(IRenderManager renderer, Vector2 pos)` is called when its turn comes, and from there it uses the immediate methods below.

### Immediate drawing (inside an `IRenderable`)

These draw right away, into the batch of the queue entry being drawn and with its shader. They are only meant to be called from an `IRenderable.Draw`:

```csharp
public void Draw(IRenderManager renderer, Vector2 pos)
{
    renderer.DrawSprite(sprite2D, pos);
    renderer.DrawString(label2D, pos);
    renderer.DrawTexture(rawTexture, pos);
    renderer.DrawQuad(texture, sourceRect, pos, Color.White);
}
```

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

`Label2D` draws text with optional shadow and outline. It can be driven by a raw `SpriteFontBase`, or by a size/family/`FontVariant` resolved through `IFontManager` for you (see [Fonts](Fonts.md)).

```csharp
var label = new Label2D("Hello, World!", size: 20f, family: "Arial", FontVariant.Bold)
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

// Default family/size
var title = new Label2D("Game Over", size: 32f);
_renderer.Submit(title, position);
```

Text in the world is drawn sharp whatever the zoom or the window size: its glyphs are made at the size they end up on screen instead of being stretched. For a pixel art font that should scale with the world like a sprite does, blocky pixels and all, set `Pixelated = true` on the label.

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

`offset` draws the sprite and its layers away from the transform position without moving the entity. Use it to keep a
tall entity's position on the ground (its feet) while the sprite stands above it. It does not turn with the transform
angle.

```yaml
components:
  - type: Sprite
    key: player/idle   # 32x64
    offset: [0, -32]   # the bottom of the sprite sits on the transform position
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

This is a separate concept from the `Unshaded` **shader** used by sprites (see [Unshaded Sprites in Lighting.md](Lighting.md#unshaded-sprites)). Shapes can't hold a shader, so `Unshaded` is just a bool that marks the shape as exempt from lighting. As with unshaded sprites, `Layer`/`Depth` are still respected normally against everything else in the scene.

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

The camera transform of the viewport being drawn is applied automatically to everything in the queue. `Camera2D` from IoC and `GameClient.Camera` are the main viewport's camera; every other viewport has its own in `RenderViewport.Camera`.

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
