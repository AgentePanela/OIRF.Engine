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
RawTexture? frame = cctv.Frame;                  // last frame drawn, null until the first one

_renderer.RemoveViewport(cctv);
```

| Member | Description |
|---|---|
| `Size` | The size the camera centers on, in world units at zoom 1 |
| `RenderScale` | Target pixels per world unit at zoom 1 |
| `TargetSize` | Target size in pixels. Defaults to `Size * RenderScale`; a different one keeps the view centered and crops or shows more |
| `ClearColor` | What the target is cleared to (the scene background when null) |
| `Enabled` | A disabled viewport is not drawn |
| `PostEffects` | Shaders the finished frame is drawn through, in order (see [Shaders](Shaders.md#on-the-whole-screen-post-effects)) |
| `Frame` | The last frame drawn, as a `RawTexture` ready to submit. Ask for it again each frame instead of keeping it |
| `Resize(size, renderScale, targetSize)` | Changes the three above; the target is recreated on the next draw |

`IRenderManager.MainViewportScaling` says how the main viewport follows the window: `Scaled` (the game resolution fit to the window, the default), `Native` (one world unit per window pixel) or `Manual` (the engine leaves it alone). With `PresentMainViewport = false` the main viewport is still drawn into its target but not put on the window, and the engine UI is skipped - that is how the editor shows the world inside one of its own panels.

### Showing a viewport in the UI

`ViewportControl` is a UI control that shows what a viewport sees: a camera feed, a monitor, a minimap.

```csharp
var cam = _renderer.CreateViewport("Security", new Vector2(320, 180));
cam.Camera.Zoom = 0.5f;
cam.Camera.LookAt(somewhere);

window.AddChild(new ViewportControl { Viewport = cam });
```

| Property | Default | |
|---|---|---|
| `Viewport` | `null` | The viewport to show |
| `AutoResize` | `true` | The viewport takes the size of the control and is drawn at the resolution of the screen: one world unit per UI unit at zoom 1, zoom with its camera. When false the viewport keeps its own size and the three options below say how its frame is placed |
| `Stretch` | `true` | Without `AutoResize`: scales the frame to fill the control. When false it is shown at the size of the viewport |
| `KeepAspect` | `true` | With `Stretch`: both axes scale by the same amount, so the frame grows as much as fits without being distorted, centered in the control |
| `IntegerScale` | `false` | With `Stretch`: rounds the scale down to a whole number, so pixel art keeps even pixels |
| `AutoEnable` | `true` | The viewport is only drawn while the control is visible, so a closed window costs nothing |
| `Tint` | white | |

`Stretch`, `KeepAspect` and `IntegerScale` are style fields (`stretch`, `keepAspect`, `integerScale`), like the ones of `EntityView`. A low resolution viewport scaled up this way is drawn with the control's `Sampler`: set it to `Point` to keep the pixels sharp.

```csharp
// a fixed 320x180 feed, as big as fits in the window without distorting it
var cam = _renderer.CreateViewport("Security", new Vector2(320, 180));
window.AddChild(new ViewportControl { Viewport = cam, AutoResize = false, Sampler = TextureSampling.Point });
```

`ScreenToWorld(point)` and `WorldToScreen(world)` on the control convert between a point of the UI (the space of `Bounds` and of the mouse events) and the world seen through it, wherever in the control the frame ends up.

The control shows the viewport, it does not own it: call `IRenderManager.RemoveViewport` when you are done with it.

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
_renderer.Submit(mySprite, position, _shaders.GetSharedInstance("Grayscale"));
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

### Textures that are not sprites

Anything that is not being handled by the atlas (the frame of another viewport, the screen texture an overlay gets) is drawn as a `RawTexture`:

```csharp
if (cctv.Frame is { } frame)
{
    frame.Size = new Vector2(320, 180);   // stretch it, whatever size the viewport renders at
    frame.Layer = 5;
    _renderer.Submit(frame, new Vector2(100, 100), shader);
}
```

When creating your own textures prefer to add they in the TextureAtlas making them a `Sprite2D` instead of using `RawTextures` for everything.

### Screen-to-World Conversion

```csharp
Vector2 worldPos = _renderer.ScreenToWorld(screenPosition);
```

---

## Render overlays

A `RenderOverlay` draws at one point of every viewport's frame, outside of the entities: under the world, over it, or on the finished image. It is the place for what is not "an entity being drawn": a background, a field of view mask, markers over everything, an effect on the screen.

```csharp
public sealed class DamageFlashOverlay : RenderOverlay
{
    public override RenderStage Stage => RenderStage.Screen;

    public float Strength;

    public override void Draw(in RenderOverlayArgs args)
    {
        if (Strength <= 0f)
            return;

        // Screen stage: positions are pixels of the viewport's target
        args.Renderer.DrawRect(args.ScreenBounds, fillColor: Color.Red * Strength);
    }
}

// once, e.g. when a system inits
_renderer.AddOverlay(_flash = new DamageFlashOverlay());

// to stop
_flash.Enabled = false;          // or _renderer.RemoveOverlay(_flash)
```

An overlay draws exactly like a draw system does: by submitting to `args.Renderer` (`Submit`, `DrawRect`, `DrawCircle`...). What it submits is sorted by layer and depth among what the same stage submitted, and drawn when the stage ends.

### Stages

In the order they happen for each viewport:

| `RenderStage` | Positions are | Drawn |
|---|---|---|
| `BelowWorld` | world | On the cleared target, before anything of the world |
| `World` | world | Together with what the draw systems submit, sorted with it by layer and depth |
| `LightMap` | world | **Into** the light map, after the lights and before the world reads it, adding up. See [Lighting](Lighting.md#drawing-into-the-light-map) |
| `AfterLighting` | world | Over the lit world, after the field of view and bloom: not darkened by the light |
| `Screen` | pixels of the target, `(0, 0)` top left | Over everything. The viewport's post effects run right after |

`BelowWorld` and `World` are lit like the rest of the world. `LightMap` happens first of all, before the world target is set, and cannot ask for the screen texture.

Text submitted in the `Screen` stage is in pixels too, so a `Label2D` there has the size its font says.

### What an overlay gets

| `RenderOverlayArgs` | |
|---|---|
| `Renderer` | Where to submit |
| `Viewport`, `Camera` | The viewport being drawn and its camera |
| `Stage` | The stage being drawn |
| `WorldBounds` | The world area the viewport shows |
| `ScreenBounds` | The whole target in pixels, for the `Screen` stage |
| `DeltaTime` | |
| `ScreenTexture` | The frame so far as a `RawTexture`, only when the overlay asks for it (below) |

### Options

| Member | Default | |
|---|---|---|
| `Stage` | required | When it draws |
| `Order` | `0` | Among overlays of the same stage, lower draws first |
| `Enabled` | `true` | |
| `AppliesTo(viewport)` | every viewport | Return false to skip one, e.g. `=> viewport == _renderer.MainViewport` |
| `RequestScreenTexture` | `false` | See below |

Overlays run for every viewport, the main one and any other, unless `AppliesTo` says otherwise.

### Reading the screen

A target cannot be read while it is being drawn into. An overlay that needs what was drawn so far overrides `RequestScreenTexture`, and gets it in `args.ScreenTexture`:

```csharp
public sealed class UnderwaterOverlay : RenderOverlay
{
    private readonly ShaderInstance _shader;

    public override RenderStage Stage => RenderStage.Screen;
    public override bool RequestScreenTexture => true;

    public UnderwaterOverlay(ShaderInstance shader) => _shader = shader;

    public override void Draw(in RenderOverlayArgs args)
    {
        // draw the frame again over itself, through the shader, on the left half only
        var frame = args.ScreenTexture!.Value;
        frame.Region = new Rectangle(0, 0, args.ScreenBounds.Width / 2, args.ScreenBounds.Height);
        args.Renderer.Submit(frame, Vector2.Zero, _shader);
    }
}
```

The frame the overlay draws on already has everything that was there, so it only needs to draw what changes. Asking for the screen texture costs a copy of the whole frame each time, and is not available in the `World` stage.

To run the whole frame through a shader there is something simpler: the viewport's `PostEffects`, see [Shaders](Shaders.md#on-the-whole-screen-post-effects).

---

## Masks

A mask limits where things are drawn: only inside a shape, or everywhere but inside it. Anything can be the shape, and anything can be cut by it.

```csharp
// 1. the shape: nothing is drawn, the window sprite becomes mask 1
using (_renderer.BeginMask(MaskMode.Write, 1))
    _renderer.Submit(windowSprite, position);

// 2. drawn only where mask 1 is
using (_renderer.BeginMask(MaskMode.Inside, 1))
    _renderer.Submit(skySprite, position);

// 3. drawn everywhere except where mask 1 is
using (_renderer.BeginMask(MaskMode.Outside, 1))
    _renderer.DrawRect(area, fillColor: Color.Black * 0.5f);
```

Everything submitted inside the `using` block gets the mode, whatever it is (sprite, text, shape, `RawTexture`); what is submitted outside of one is drawn as usual.

| `MaskMode` | |
|---|---|
| `Write` | Draws nothing, adds to the mask instead |
| `Inside` | Drawn only where the mask is |
| `Outside` | Drawn only where the mask is not |

Rules:

- **Order matters.** The mask has to be written before what it cuts is drawn, and the queue draws by layer, then depth, then submit order. Give the `Write` the same layer and depth as what it cuts (or a lower one) and submit it first.
- **Masks are numbered** from 1 to 255, so several can be in use at once. A pixel holds one number: writing mask 2 over mask 1 leaves 2 there.
- **A mask belongs to one viewport and one frame.** It is empty at the start of each viewport's frame.
- **A sprite writes its own shape**: only its mostly opaque texels count, not its rectangle. A sprite that writes with a shader of its own uses that shader instead, and counts wherever the shader does not `clip`.
- **A shape writes its bounding box**, whatever it is: a `DrawCircle` used as a mask masks a square. Use a sprite for a mask that is not a rectangle.
- **Reading the screen empties the masks.** An overlay that asks for the screen texture, and the post effects, continue on another target: write the mask again after.

Each change of mask mode ends a batch, like a change of shader does.

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

### A shader over the whole entity

A shader set on a sprite or a layer is applied to that one piece. A **post shader** is applied to the entity as one image: the sprite and all its layers are first drawn into a texture of their own, and that texture goes on screen through the shader.

```csharp
_sprite.SetPostShader(uid, "Glow", padding: 4);
_sprite.SetPostShaderParam(uid, "GlowColor", Color.Yellow);

_sprite.SetPostShader(uid, null);     // off
```

Use it when the shader has to see the entity whole (an outline around the whole character and not around each piece of clothing) or has to draw past the edge of the sprite: `padding` is how many empty pixels it gets around the entity for that.

In the shader, `LocalUV` runs 0..1 over that image (padding included) and `TexelSize` is one pixel of it. Per sprite parameters work as usual.

An entity can have several, applied one after the other, each to what the one before it drew:

```csharp
_sprite.AddPostShader(uid, "Outline", padding: 2);   // first
_sprite.AddPostShader(uid, "Glow", padding: 4);      // then this one, over the outlined image

_sprite.SetPostShaderParam(uid, "Glow", "GlowColor", Color.Yellow);   // one of them
_sprite.SetPostShaderParam(uid, "Strength", 0.5f);                     // every one that has it

_sprite.RemovePostShader(uid, "Outline");
_sprite.ClearPostShaders(uid);
```

| Method | |
|---|---|
| `SetPostShader(uid, shader, padding)` | Replaces whatever the entity had with this one. `null` removes them all |
| `AddPostShader(uid, shader, padding, order)` | Adds one. Lower `order` is applied first, equal ones in the order they were added. Adding one it already has changes its padding and order |
| `RemovePostShader(uid, shader)` / `ClearPostShaders(uid)` | |
| `HasPostShader(uid, shader)` / `GetPostShaders(uid)` | |
| `SetPostShaderParam(uid, shader, name, value)` | A parameter of one of them |
| `SetPostShaderParam(uid, name, value)` | The same parameter on all of them, ignored by the ones that do not have it |

Use `order` for something that has to stay on top of whatever else gets added later, whoever adds it and when: an outline with a high order always goes around the entity as it ends up, even if another post shader is added after it.

The paddings add up: with the two above the image has 6 empty pixels around the entity, since the glow draws around the outline. The same shader prototype cannot be on an entity twice; make a second prototype on the same `.fx` for that.

It is local (nothing replicates) and it has a cost: a texture per entity and one more draw of it for each post shader, every frame it is on screen. It is meant for a few entities at a time, not for every wall of a map. The first frame an entity is on screen it is drawn without the shaders, the image is ready from the next one on.

---

## Shapes

Beyond sprites and text, `RenderManager` can draw vector shapes directly: circles, rectangles, lines, polygons, and more, without needing a texture. These are backed by [Apos.Shapes](https://github.com/Apos-Games/Apos.Shapes) and queued through the same submit pipeline as everything else, so they respect `Layer`/`Depth` like any other renderable. They are never lit: Apos.Shapes draws them with its own effect, which does not read the light map.

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

Since shapes are never lit, the flag changes nothing for now; it is kept for when they are.

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
