# Graphics Pipeline & RenderManager Internals

This document explains OIRF Engine's client-side rendering pipeline, covering queue management, draw call batching, shader states, and viewport scaling.

---

## 1. The Rendering Lifecycle

The world is drawn into render targets, one per `RenderViewport`, and only then put on the window. In the game draw tick (`GameClient.Draw`):

```
SceneManager.Draw() ───────────────► the current scene submits (goes to the main viewport)
                                            │
                                            ▼
RenderManager.RenderFrame() ───────► SyncMainViewport(): fit the main viewport to the window
                                            │
                                            ▼
  for each enabled viewport, main last (DrawViewport):
    SetRenderTarget + Clear
    Camera.CacheFrame()
    overlays: BelowWorld
    EntityManager.DrawSystems(ctx) ──► systems Submit() what that viewport sees
    overlays: World ─────────────────► into the same queue
    DrawQueue() ─────────────────────► sort, then batch by texture, shader, sampler and blend
    overlays: BeforeLighting
    (light rework: the light target is multiplied over the world here)
    overlays: AfterLighting
    overlays: Screen ────────────────► identity transform, pixels of the target
    ApplyPostEffects()
                                            │
                                            ▼
  SetRenderTarget(null) + Clear ─────► main viewport target drawn onto the window
                                            │
                                            ▼
InterfaceManager.Draw() ───────────► UI over it
```

Secondary viewports are drawn before the main one so the main one can use their targets. Whatever is in the queue when `RenderFrame` starts was submitted outside of a viewport draw; it is set aside while the secondary viewports draw and handed to the main one.

The backbuffer is created with the default `RenderTargetUsage` (its contents are not preserved): every target switch happens before the window is drawn to, so nothing has to survive one.

---

### Overlays and reading the frame

`DrawOverlays(viewport, stage)` calls the overlays of one stage. Outside of the `World` stage they submit into their own buffer (`_overlayQueue`, swapped in as `_queue` while they run), so what a scene submitted for the world is not drawn early; the buffer is drawn when the stage ends.

Each viewport has two targets of the same size, `ColorTarget` and a scratch one created the first time it is needed. Reading the frame is always the same move, `RenderViewport.SwapTargets()`: the scratch becomes the target being drawn into and the old one becomes readable.

* An overlay with `RequestScreenTexture`: the queue is drawn, the targets are swapped, the frame is copied onto the new target (so the overlay draws over a complete image), the overlay runs with the old target as `ScreenTexture`, and what it submitted is drawn right away, before anything else can swap again.
* A post effect: the targets are swapped and the old one is drawn over the whole new one through the shader. No copy is needed, the shader output covers everything.

Nothing is copied back: `ColorTarget` is simply whichever target ended up holding the frame, which is why it can change from one frame to the next.

`RenderViewport.LightTarget` and the `BeforeLighting` / `AfterLighting` stages are the hooks the lighting rework plugs into; nothing fills or reads the light target yet.

---

## 2. Render Queue & Batching (`DrawQueue()`)

The rendering engine works by delaying draws rather than rendering immediately. What systems submit is kept in a `DrawCommandBuffer`.

### 1. Commands
A queue entry is a `DrawCommand` struct in an array, not an object. There are three kinds:
* **Sprite**: a textured quad. The texture, source region, transform, color, shader and the two per sprite values are all in the command; nothing is called to draw it and nothing is allocated to queue it.
* **Renderable**: an `IRenderable` that draws itself (a tilemap chunk, a `Label2D`). A struct one is boxed through a pool (`RenderPool<T>`), and the boxes are kept with the buffer they were submitted to so setting a buffer aside never returns boxes that are still waiting.
* **Shape**: an `IShapeRenderable`, drawn through Apos.Shapes' own batch.

### 2. Sorting
Sorting moves `DrawSortEntry` values (16 bytes), not the commands. The key is one 64 bit number, layer in the high half and depth in the low one, both made to sort right as unsigned; ties keep submit order through the entry's index.

### 3. `SpriteBatcher`
Sprites, text and tile quads all go through the engine's own batcher (`Graphics/Batching/SpriteBatcher.cs`), not MonoGame's `SpriteBatch`. Each vertex carries position, color, the atlas UV, a `LocalUV` (0..1 over the quad) and two free `Vector4` (`Custom0`, `Custom1`).

Quads pile up in one array and are sent with a single `DrawUserIndexedPrimitives` when something they all have to share changes: the texture, the shader instance, the sampler or the blend state. Per sprite shader values are in the vertices, so they never split a batch. That is what lets any number of sprites, each with its own values for the same shader, be one draw call.

At each flush the batcher fills the shader's engine parameters (`MatrixTransform`, `TexelSize`, `Time`, `ScreenSize`), through `EffectParameter`s the `ShaderInstance` looked up once. With no shader it uses MonoGame's `SpriteEffect`.

Text goes in through a small `IFontStashRenderer2` (`RenderManager.Strings.cs`): FontStashSharp works the glyph quads out and they are appended like any other. `DrawString` asks for the font at the size it ends up on the target (font size x render scale x zoom, in steps of 2 px) and scales it back down, so glyphs are not stretched.

Shapes are the one thing that still breaks out: the batcher is flushed, the shape batch opened, and closed again when the next sprite comes.

### 4. Stats
`RenderManager` counts what it does into `RenderStats` while the frame is drawn and publishes the finished frame in `IRenderManager.Stats`: draw calls, triangles, texture swaps and target switches (from `GraphicsDevice.Metrics`, world only), batches, quads and why each batch broke (texture, shader, sampler, blend, sprite/shape), shader instances shared and owned, post effects and their time, the memory of the viewport targets, the time each overlay took, items submitted and culled, and the time spent submitting, sorting, flushing and presenting, plus a line per viewport. The profiler overlay shows them in its RENDERING panel.

---

## 3. Viewports, Scaling & Cameras

### 1. `RenderViewport`
A viewport is a logical `Size` (what its camera centers on), a `RenderScale` (target pixels per world unit) and a `TargetSize` in pixels. Its view matrix is the camera matrix, times the scale, plus a whole-pixel translation that keeps the view centered when the target is not exactly `Size * RenderScale`.

### 2. The main viewport and the window
`ViewportAdapter` still computes the scale that fits the game resolution to the window (`FitScaleOuter` / `FitScaleInteger`). `SyncMainViewport()` turns that into the main viewport:
* `Size` is the game resolution and `RenderScale` the adapter's scale.
* `TargetSize` is the scaled size clamped to the window, so when the scale crops (outer fit) only the visible part is drawn.
* `ScreenOffset` is where the target sits on the window: centered, which is what leaves the letterbox bars when the scaled game is smaller than the window.

### 3. Coordinate Translation
`Camera2D.ScreenToWorld` / `WorldToScreen` go through the viewport: subtract `ScreenOffset`, then the inverse view matrix. `Camera2D.CacheFrame()` stores the visible world box (taking rotation into account) for the `IsOnScreen` checks, and runs for each viewport before its draw systems.

---

## 4. Shaders (`ShaderManager`, `ShaderInstance`)

The content facing side is in `Docs/Content/Shaders.md`. Underneath:

* `Engine.ResourcesBuilder` compiles each `.fx`. Before that, `ShaderLightingInjector.GenerateRoot` copies every `.fxh` found under the `Shaders/` folder of any resources folder next to the shaders being built, which is how a content shader includes the engine's `Sprite.fxh`. The headers are part of each shader's hash, so editing one rebuilds everything.
* `ShaderManager` loads the compiled effects by file name and creates one `ShaderInstance` per `shader` prototype the first time it is asked for: a clone of the effect with the prototype's `params` applied, its `lit` and `blend`, and the `perSprite` map parsed into slots. Starting values of per sprite parameters are kept on the instance (`DefaultCustom0/1`) instead of being set on the effect.
* `SpriteSystem` keeps, per sprite and per layer, the shader instance, the parameters set through `SetShaderParam` (by name) and the two custom values built from them. They are rebuilt only when the shader or a parameter changes. A parameter with no slot makes the target `Duplicate()` its instance, which is disposed when the shader changes or the sprite is removed.
* Batches are compared by instance, so two prototypes on the same `.fx` do not batch together, and neither does a duplicated instance with anything else.
