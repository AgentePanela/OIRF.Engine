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
  BeforeFrame ───────────────────────► textures drawn ahead (post shaders of entities)
  PrepareLighting() ─────────────────► the lights of every lit viewport, once (section 5)
                                            │
                                            ▼
  for each enabled viewport, main last (DrawViewport):
    Camera.CacheFrame()
    BuildLightMap() ─────────────────► its light map, overlays: LightMap
    BuildFovMask() ──────────────────► what its eye does not see
    SetRenderTarget + Clear
    overlays: BelowWorld
    EntityManager.DrawSystems(ctx) ──► systems Submit() what that viewport sees
    overlays: World ─────────────────► into the same queue
    DrawQueue() ─────────────────────► sort, then batch; every sprite reads the light map
    DrawFov() ───────────────────────► fov.color over what is out of sight
    ApplyBloom()
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

Each viewport has two targets of the same size, `Target` and a scratch one created the first time it is needed. Reading the frame is always the same move, `RenderViewport.SwapTargets()`: the scratch becomes the target being drawn into and the old one becomes readable.

* An overlay with `RequestScreenTexture`: the queue is drawn, the targets are swapped, the frame is copied onto the new target (so the overlay draws over a complete image), the overlay runs with the old target as `ScreenTexture`, and what it submitted is drawn right away, before anything else can swap again.
* A post effect: the targets are swapped and the old one is drawn over the whole new one through the shader. No copy is needed, the shader output covers everything.

Nothing is copied back: `Target` is simply whichever target ended up holding the frame, which is why it can change from one frame to the next.

The light map and the field of view mask are built before the world target is set: switching away from it would throw away what was drawn in it.

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
Sprites, text and tile quads all go through the engine's own batcher (`Graphics/Batching/SpriteBatcher.cs`), not MonoGame's `SpriteBatch`. Each vertex carries position, color, the atlas UV, a `LocalUV` (0..1 over the quad), two free `Vector4` (`Custom0`, `Custom1`) and the `Light` `Vector4` (section 5).

Quads pile up in one array and are sent with a single `DrawUserIndexedPrimitives` when something they all have to share changes: the texture, the shader instance, the sampler or the blend state. Per sprite shader values are in the vertices, so they never split a batch. That is what lets any number of sprites, each with its own values for the same shader, be one draw call.

At each flush the batcher fills the shader's engine parameters (`MatrixTransform`, `TexelSize`, `Time`, `ScreenSize`, and the light map ones), through `EffectParameter`s the `ShaderInstance` looked up once. With no shader it uses MonoGame's `SpriteEffect`.

Text goes in through a small `IFontStashRenderer2` (`RenderManager.Strings.cs`): FontStashSharp works the glyph quads out and they are appended like any other. `DrawString` asks for the font at the size it ends up on the target (font size x render scale x zoom, in steps of 2 px) and scales it back down, so glyphs are not stretched.

Shapes are the one thing that still breaks out: the batcher is flushed, the shape batch opened, and closed again when the next sprite comes.

### 4. Stats
`RenderManager` counts what it does into `RenderStats` while the frame is drawn and publishes the finished frame in `IRenderManager.Stats`: draw calls, triangles, texture swaps and target switches (from `GraphicsDevice.Metrics`, world only), batches, quads and why each batch broke (texture, shader, sampler, blend, mask, sprite/shape), shader instances shared and owned, post effects and their time, the memory of the viewport targets, the time each overlay took, items submitted and culled, and the time spent submitting, sorting, flushing and presenting, plus a line per viewport. The profiler overlay shows them in its RENDERING panel.

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

### Masks

A mask is the stencil of the viewport's target, which is why the targets are created with `Depth24Stencil8`. `BeginMask` only sets a mode and an id that every `Submit` stamps on its command; the returned `MaskScope` puts the previous ones back.

When the queue is drawn, each command's mode becomes a `DepthStencilState` (cached per mode and id in `RenderManager.Masks.cs`): `Write` always passes and replaces the stencil with the id, `Inside` passes where the stencil equals it, `Outside` where it does not. While writing, the blend state has no color channels enabled, so only the stencil changes. A change of stencil state flushes the batcher, and restarts the shape batch.

The stencil is written for every pixel a quad covers, so a sprite written with the plain sprite effect would mask its whole rectangle. A sprite in `Write` mode with no shader of its own is drawn with `EngineSpriteMask.fx` (in `EngineResources/Shaders`, compiled along with the content's), which clips texels under half alpha. Apos.Shapes has no such thing, hence shapes masking their bounding box.

The clear at the start of `DrawViewport` clears the stencil too. `SwapTargets` moves to a target with its own stencil, which is why masks do not survive it.

### Drawing into a texture before the frame

Switching render targets in the middle of a viewport would discard what was drawn in it so far, so anything that needs a texture of its own is drawn before any viewport is: `RenderFrame` raises `BeforeFrame` first, and `RenderToTarget(target, transform, state, submit)` draws what `submit` submits into `target`, through its own queue and with `transform` in place of a viewport's matrix. Both are internal to the engine for now.

The post shader of `SpriteSystem` (`SpriteSystem.PostShader.cs`) is built on it. For each entity with a post shader that some viewport drew last frame, `DrawPostTextures` measures the box around its sprite and layers with the entity at the origin and unrotated, makes sure its texture is big enough (they only grow, in steps of 32), and submits the entity through the same `SubmitWithLayers` the world uses, with `local: true` leaving the rotation out. With several post shaders, every one but the last is then applied right there: the image is drawn through the shader into a second texture of the same size and the two are swapped, once per shader. In `Draw`, such an entity is a single `RawTexture` of the resulting texture, rotated as a whole and drawn through the last post shader. An entity whose texture is not ready yet falls back to being drawn piece by piece, without the shader, for that frame.

---

## 4. Shaders (`ShaderManager`, `ShaderInstance`)

The content facing side is in `Docs/Content/Shaders.md`. Underneath:

* `Engine.ResourcesBuilder` compiles each `.fx`. Before that, `ShaderLightingInjector.GenerateRoot` copies every `.fxh` found under the `Shaders/` folder of any resources folder next to the shaders being built, which is how a content shader includes the engine's `Sprite.fxh`. The headers are part of each shader's hash, so editing one rebuilds everything.
* `ShaderManager` loads the compiled effects by file name and creates one `ShaderInstance` per `shader` prototype the first time it is asked for: a clone of the effect with the prototype's `params` applied, its `lit` and `blend`, and the `perSprite` map parsed into slots. Starting values of per sprite parameters are kept on the instance (`DefaultCustom0/1`) instead of being set on the effect.
* `SpriteSystem` keeps, per sprite and per layer, the shader instance, the parameters set through `SetShaderParam` (by name) and the two custom values built from them. They are rebuilt only when the shader or a parameter changes. A parameter with no slot makes the target `Duplicate()` its instance, which is disposed when the shader changes or the sprite is removed.
* Batches are compared by instance, so two prototypes on the same `.fx` do not batch together, and neither does a duplicated instance with anything else.

---

## 5. Lighting (`Graphics/Lighting`)

The content facing side is in `Docs/Content/Lighting.md`. `RenderManager.Lighting.cs` only reads the CVars, works out what each viewport's light map covers and builds the light maps; the work is done by `LightingSystem` (an entity system, plugged in as the internal `ILightRenderer`), split in partials: collecting the lights (`LightingSystem.cs`), drawing them (`.Renderer.cs`) and the field of view (`.Fov.cs`).

### The light map

One `RenderTarget2D` per viewport (`LightTarget`), world aligned: it covers the view plus `light.margin`, at `light.resolution` texels per world unit, its origin snapped to the texel grid so the light does not shimmer when the camera moves. It holds `light / light.overbright`, so lights can go over 1 in 8 bits.

Sprites read it themselves. Every vertex carries a `Light` `Vector4` (`SpriteVertex`, `TEXCOORD4`): xy the world position the light is read at, z the emission, w the highest y it is read at. `SpriteSystem` fills it from the sprite's `lightBase`/`lightOffset`/`emission` (`TextureLighting`). `SPRITE_TECHNIQUE` wraps the content's pixel shader with `ApplyLighting` (`TextureLighting.fxh`), which samples the light map on `s1` at that position, through `LightMapTransform`, and multiplies. So lighting costs no pass of its own and does not break batches; the light map is bound once per viewport (`SpriteBatcher.LightMap`).

### Drawing the lights

`Prepare` runs once per frame for all viewports:

1. **Collect.** Every `PointLight` reaching a light map, after `LightDrawingEvent` (one reused instance). Occluders are indexed by `OccluderSystem` in cells of 256 world units, with their outlines merged (shared edges between opaque boxes cancel) and only the edges facing away from a light returned.
2. **Classify.** A light whose values did not change for `light.static-delay` seconds (nor the occluders in its reach, `OccluderSystem.TakeChanges`) is static; the rest, and anything `Animated`, is dynamic.
3. **Squares.** Each light is drawn into a square of an atlas page (`LightAtlas`, 2048² pages, rows of squares in steps of 32): the shadows first, into the alpha (`ShadowBatch` + `LightShadow.fx`: each edge a quad stretched away from the light, the penumbra worked out per pixel from the angle the edge covers of the source), then the light (`LightShape.fx`: falloff, cone, mask) with `InverseDestinationAlpha`, then glass multiplies it (`SeeThrough` technique, `pow(transmit, coverage)` so the pieces of an edge leave no seam). Every light of a page is one batch of each, whatever their number. Static squares stay in their own atlas until the light changes; dynamic ones go to a page that is redrawn every frame.
4. **Chunks.** Static squares are added into chunks of 512 world units (`LightChunkCache`) kept while near a viewport. A chunk is redrawn only when a static light in it changes. When the view needs more chunks than fit, the static squares are added one by one instead.

`Composite`, per viewport, clears the light map to the ambient, adds the chunks and the dynamic squares, then the sprite lights (`SpriteLightSystem` collects them once per frame; `SpriteGlow.fx` averages the frame of the layer into a soft rectangle), then the `LightMap` overlays.

### Field of view

`BuildFovMask` draws, at screen density, the shadows of the viewport's eye into a mask of its own (`FovTarget`), with the same `ShadowBatch` and a source of size 0 (hard edges). Glass is left out, and the `fovHeight` of an occluder raises its box for this mask only. After `DrawQueue`, `Fov.fx` paints `fov.color` over the view where the mask says hidden, and the radius on top.

### Bloom

`ApplyBloom` (`RenderManager.Bloom.cs`): the bright part of the frame at half size (`BloomExtract.fx`), shrunk to a quarter and an eighth, each blurred both ways (`BloomBlur.fx`, 9 taps in 5 samples), then the frame is swapped (`SwapTargets`) and the three levels are added over it (`BloomAdd.fx`). The six targets live on the viewport.

### Stats and debugging

`RenderStats.Light` (`LightStats`) is filled while the frame is drawn and shown in the profiler's LIGHTING panel. The debug window's Lighting tab lists the pages (`LightingSystem.CollectPages`). `light.debug-base` replaces the light map by stripes (`DrawBaseStripes`), which shows where each sprite reads it without touching any shader; the bases of the sprites that are not `Pixel` are gathered while the world queue is drawn and marked with dots after the bloom.
