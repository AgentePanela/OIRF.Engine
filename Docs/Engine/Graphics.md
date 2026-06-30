# Graphics Pipeline & RenderManager Internals

This document explains OIRF Engine's client-side rendering pipeline, covering queue management, draw call batching, shader states, and viewport scaling.

---

## 1. The Rendering Lifecycle

OIRF Engine groups draw calls to minimize GPU state changes. In the game draw tick:

```
EntityManager.Draw() ──────────────► Submit draw entries (Sprites, text, chunks)
                                            │
                                            ▼
RenderManager.Submit() ────────────► Add to SortedDictionary<int, List<RenderQueue>>
                                            │
                                            ▼
RenderManager.DrawQueue() ─────────► Begin SpriteBatch with camera matrix
                                            │
                                            ▼
[Render Loop] ─────────────────────► Batch draw calls by Shader and SamplerState
                                            │
                                            ▼
RenderManager.End() ───────────────► Flush vertices to GPU
```

---

## 2. Render Queue & Batching (`DrawQueue()`)

The rendering engine works by delaying draws rather than rendering immediately. When systems draw, they submit an `IRenderable` instance (like a `SpriteComponent` or `TilemapChunk` renderable).

### 1. Zero-Allocation Struct Submission
Boxing structures inside reference objects causes heavy garbage collector (GC) load. To prevent this, `Submit<T>` rents wrapper class instances from a pool (`RenderPool<T>.Rent()`). After drawing, these wrappers are returned (`ReturnToPool()`).

### 2. State Batch Sorting
Draw submissions are placed inside `_renderQueue`, sorted by layer ID (`int`). When rendering the queue:
* The system opens the batch with the camera translation view matrix (`_camera.GetViewMatrix()`).
* It loops through the items. If an item requests a different shader (`Effect`) or `SamplerState` (texture filter mode) than the current one, the system ends the current batch, starts a new batch with the new states, and continues.
* This automatically clusters drawing passes by layer, shader, and sampler, keeping GPU draw call totals low.

---

## 3. Viewport Scaling & Adapters (`ViewportAdapter`)

To support resizing while keeping aspect ratios constant, the engine translates graphics:

### 1. Aspect Ratio Letterbox
* `ViewportAdapter` maintains virtual dimensions (e.g. `VirtualWidth` / `VirtualHeight`).
* When the window client size changes, the adapter recalculates scaling matrices:
  ```csharp
  int viewportX = (pp.BackBufferWidth - _viewport.VirtualWidth) / 2;
  int viewportY = (pp.BackBufferHeight - _viewport.VirtualHeight) / 2;
  ```
  This positions the game viewport centered on screen, displaying black bars (letterboxing) on the borders if aspect ratios do not align.

### 2. Coordinate Translation
The camera handles translating virtual coordinates to screen space. Developers can call `RenderManager.ScreenToWorld(Vector2 screenPos)` to convert screen clicks back into world coordinates.

---

## 4. Shaders & Effects (`ShaderManager`)

Custom shader effects are handled in the graphics pipeline:
* `ShaderManager` loads shader files (`.fx`) from the resources directory.
* Shaders are resolved via prototype key mappings.
* Custom parameters are passed into the `Effect` instance, and the shader is submitted alongside the sprite to the `RenderManager`.
* `RenderManager` binds the shader effect parameter into the native MonoGame `SpriteBatch.Begin()` call.
