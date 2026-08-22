# Profiling

The engine measures where a frame goes and writes it out as a text report.
`ProfilerReport.Dump()` renders the report and saves it to
`logs/profile-{timestamp}.log` under the user data path, returning the full
path.

```csharp
if (InputManager.KeyPressed(Keys.F2))
    Log.Debug($"Profiler report: {ProfilerReport.Dump()}");
```

## Why there are three ways to measure

`SpriteBatch.End()` and `SetRenderTarget()` only queue commands. The GPU work
happens later and shows up as a stall inside `Present()`, so a CPU timer around
a render pass measures how long it took to *submit* the pass, not what it cost.
That is why a frame can report 0.4ms of rendering while running at 30 fps.

| Tool | Measures | Cost |
|---|---|---|
| Scope timers | CPU submission | free, always on |
| `engine.profiler.gpu-sync` | real GPU cost per pass | large, diagnostic only |
| Pass isolation sweep | end to end cost of a feature | a few seconds |

Start with the **VERDICT** section. If it says GPU/vsync bound, the CPU numbers
below it are not the problem and you want the fill rate section and the sweep.

## Reading the report

- **VERDICT** — CPU bound or GPU bound, plus the five worst scopes.
- **FRAME PHASES** — the scope tree in execution order, indented by nesting,
  with ms, share of the frame and bytes allocated. `calls` above 1.0 means
  MonoGame ran several Updates for one Draw catching up on the fixed timestep.
- **DRAW CALLS / BATCH BREAKS** — draw calls by kind and, for each batch break,
  what caused it. `draw calls per batch` is the number to watch; if it is low,
  the top transition list names the shader or sampler doing the damage.
- **RENDER TARGETS** — size, format, VRAM, binds and clears per frame, plus
  reallocations (a target being resized every frame is a bug).
- **FILL RATE** — pixels each pass shades, estimated analytically. In a 2D
  engine with dynamic lighting this is usually the real GPU cost. `overdraw`
  is the total against one screenful.
- **SUBMISSION BY DRAW SYSTEM** — what each draw system queued and how much it
  culled. A low cull percentage with a lot of submits means the culling is not
  doing its job.
- **ECS SYSTEMS** — per system update/draw, worst case and allocation.

## Scopes

Wrap anything worth naming. Scopes nest, and a scope that is off costs nothing.

```csharp
using (_profiler.Scope("update/ai"))
    RunAi(dt);

// GpuScope also drains the pipeline when gpu-sync is on
using (_profiler.GpuScope("draw/water"))
    DrawWater();
```

## Pass isolation sweep

The sweep measures each render feature by turning it off and timing the
difference, so it catches CPU and GPU cost together without needing a GPU timer
query. It runs a set of configurations for `engine.profiler.sweep-frames` frames
each and reports the median frame time and how much each feature costs.

It disables vsync and the fixed timestep while it runs, because otherwise every
configuration would be pinned to the refresh rate and read as "no difference".
Everything is restored afterwards.

```csharp
GameClient.Sweep.Start();     // then poll GameClient.Sweep.Running
```

## CVars

| CVar | Default | What it does |
|---|---|---|
| `engine.profiler.enabled` | `true` | Master switch for scope timing. |
| `engine.profiler.window` | `120` | Frames kept for the rolling averages. |
| `engine.profiler.gpu-sync` | `false` | Drains the GPU after each render pass so its scope measures GPU work. Inflates the frame time on purpose - the numbers are only comparable to each other. |
| `engine.profiler.sweep-frames` | `60` | Frames measured per sweep configuration. |
| `engine.system-profiller-top` | `10` | Systems returned by `SystemsProfiler.GetTop()`. |

With `gpu-sync` on, the first render pass of the frame absorbs whatever the
previous frame left queued, so `draw/clear` reads high and should be ignored.
Everything after it is that pass' own GPU cost.

## Measure with vsync off

With vsync or the fixed timestep on, the frame time is capped and the report can
only show how the frame is *shared*, not how much headroom is left. To see the
real ceiling:

```csharp
Graphics.SynchronizeWithVerticalRetrace = false;
IsFixedTimeStep = false;
Graphics.ApplyChanges();
```

The report header always states which of the two are on.
