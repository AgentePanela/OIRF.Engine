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
  for each enabled viewport, main last:
    SetRenderTarget + Clear
    Camera.CacheFrame()
    EntityManager.DrawSystems(ctx) ──► systems Submit() what that viewport sees
    DrawQueue() ─────────────────────► sort, then batch by shader and sampler
                                            │
                                            ▼
  SetRenderTarget(null) + Clear ─────► main viewport target drawn onto the window
                                            │
                                            ▼
InterfaceManager.Draw() ───────────► UI over it
```

Secondary viewports are drawn before the main one so the main one can use their targets. Whatever is in the queue when `RenderFrame` starts was submitted outside of a viewport draw; it is set aside while the secondary viewports draw and handed to the main one.

The backbuffer is no longer created with `RenderTargetUsage.PreserveContents`: every target switch happens before the window is drawn to, so nothing has to survive one.

---

## 2. Render Queue & Batching (`DrawQueue()`)

The rendering engine works by delaying draws rather than rendering immediately. When systems draw, they submit an `IRenderable` instance (like a `Sprite2D` or a tilemap chunk renderable).

### 1. Zero-Allocation Struct Submission
Boxing structures inside reference objects causes heavy garbage collector (GC) load. To prevent this, `Submit<T>` rents wrapper class instances from a pool (`RenderPool<T>.Rent()`). After drawing, these wrappers are returned (`ReturnToPool()`). The rented boxes are kept together with the queue they were submitted to (`QueueBuffer`), so setting a queue aside never returns boxes that are still waiting to be drawn.

### 2. State Batch Sorting
Entries are sorted by layer, then depth, then submit order. When rendering the queue:
* Every batch starts with the view matrix of the viewport being drawn (`RenderViewport.GetViewMatrix()`).
* It loops through the items. If an item requests a different shader (`Effect`) or `SamplerState` (texture filter mode) than the current one, or switches between sprites and shapes, the system ends the current batch and starts a new one.

### 3. Stats
`RenderManager` counts what it does into `RenderStats` while the frame is drawn and publishes the finished frame in `IRenderManager.Stats`: draw calls, triangles, texture swaps and target switches (from `GraphicsDevice.Metrics`, world only), batches and why each one broke, items submitted and culled, and the time spent submitting, sorting, flushing and presenting, plus a line per viewport. The profiler overlay shows them in its RENDERING panel.

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

## 4. Shaders & Effects (`ShaderManager`)

Custom shader effects are handled in the graphics pipeline:
* `ShaderManager` loads shader files (`.fx`) from the resources directory.
* Shaders are resolved via prototype key mappings.
* Custom parameters are passed into the `Effect` instance, and the shader is submitted alongside the sprite to the `RenderManager`.
* `RenderManager` binds the shader effect parameter into the native MonoGame `SpriteBatch.Begin()` call.
