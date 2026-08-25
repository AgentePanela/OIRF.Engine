# Profiling

The engine measures where a frame goes and writes it out as a text report.
`ProfilerReport.Dump()` renders the report and saves it to
`logs/profile-{timestamp}.log` under the user data path, returning the full
path. In the game (Trieste) this is bound to **F2**:

```csharp
if (InputManager.KeyPressed(Keys.F2))
    Log.Debug($"Profiler report: {ProfilerReport.Dump()}");
```

F3 opens the live `ProfilerOverlay` (per-system update/draw bars, memory,
lighting) and F4 opens `DebugWindow`, whose "Debug Tools" tab has a `GPU
sync` checkbox and `Run Sweep`/`Dump Report` buttons - both covered below.

## Why there are three ways to measure

`SpriteBatch.End()` and `SetRenderTarget()` only queue commands. The GPU work
happens later and shows up as a stall inside `Present()`, so a CPU timer
around a render pass measures how long it took to *submit* the pass, not what
it cost. That is why a frame can report 0.4ms of rendering while running at
30 fps - and why a healthy vsync'd frame legitimately spends most of its time
"idle" in present/vsync wait, which is not the same thing as being GPU bound.

| Run | vsync | gpu-sync | What it measures |
|---|---|---|---|
| 1 | off | off | pure CPU cost, uncapped |
| 2 | off | on | CPU + GPU serialized, uncapped - closest to real GPU cost |
| 3 | on | off | the real frame the player sees |

The difference between runs 1 and 2 is the real cost of the GPU work. With
vsync on (the default), present/vsync wait is idle time the CPU spent
waiting for the next refresh, not a cost - the **VERDICT** section only calls
a frame GPU bound when gpu-sync is actually forcing present to carry GPU
cost, or when the frame overran its vsync budget outright (a real, missed
frame). The report's BUILD section always states which of the three
situations above it was generated under, and VERDICT explains what that
implies before you read anything else.

## Reading the report

- **VERDICT** — vsync-limited / GPU bound / CPU bound / missed frames, plus
  the five worst scopes and a self-check that flags a mismatch between the
  measured fps and what cpu+present implies (a sign one of the two numbers is
  wrong, not that both agree).
- **FRAME** — the frame budget: cpu avg/min/max/p95, present/vsync wait, and
  (with vsync on) the percentage of the vsync period actually used by the
  CPU. Present/vsync wait is never counted as "used".
- **FRAME PHASES** — the scope tree in execution order, indented by nesting,
  with ms, share of the frame and bytes allocated. `calls` above 1.0 means
  MonoGame ran several Updates for one Draw catching up on the fixed timestep.
- **DRAW CALLS / BATCH BREAKS** — draw call/primitive/sprite counts come
  straight from `GraphicsDevice.Metrics` around the world render queue, not
  hand-counted, so they can't quietly fall out of date. Batch breaks (shader
  switch, sampler switch, sprite↔shape) are counted at the one place they
  actually happen, `RenderManager.DrawRenderQueue`.
- **RENDER TARGETS** — size, format, VRAM, binds/clears per frame, and a
  reallocation count detected automatically whenever a name gets bound to a
  different target reference than last time (a target resizing every frame
  shows up here on its own, no manual counter needed). The tracked binds/
  clears are cross-checked against `GraphicsDevice.Metrics` at the bottom - a
  gap there means some pass is calling `SetRenderTarget`/`Clear` directly
  instead of through the tracked wrapper below, and needs to be found.
- **FILL RATE** — pixels each lighting pass shades, estimated analytically.
  In a 2D engine with dynamic lighting this is usually the dominant GPU cost.
- **LIGHTING** — shadow/light/bleed/blur breakdown, read straight from
  `LightingManager`'s last-frame fields (the same ones `ProfilerOverlay` and
  `LightingDebugTab` show live).
- **PASS ISOLATION SWEEP** — see below.
- **ECS SYSTEMS** — per-system update/draw from `SystemsProfiler`, the same
  source `ProfilerOverlay`'s right panel uses.
- **MEMORY / GC** — GC collections and bytes allocated *since boot* and
  *since the last dump*, from `SessionGCTracker`. Unlike sampling
  `GC.CollectionCount` once a frame (which is almost always zero, since
  collections don't happen every frame), this keeps two fixed baselines and
  only diffs against them when a report is actually built.

## Scopes

Wrap anything worth naming. Scopes nest, and a scope that is off costs nothing.

```csharp
using (_profiler.Scope("update/ai"))
    RunAi(dt);

// GpuScope also drains the pipeline when gpu-sync is on
using (_profiler.GpuScope("draw/water"))
    DrawWater();
```

## Tracking a render target

Every `SetRenderTarget`/`Clear` in the client should go through
`GraphicsDeviceProfilerExtensions` instead of the raw `GraphicsDevice` call,
so a new pass is tracked by construction instead of needing someone to
remember to add a counter for it:

```csharp
GameClient.GraphicsDevice.SetRenderTargetTracked(myTarget, "MyPass");
GameClient.GraphicsDevice.ClearTracked(Color.Black, "MyPass");
```

If a pass ever does bypass this, the RENDER TARGETS section's
`GraphicsDevice.Metrics` cross-check will show a gap instead of silently
reading binds/f 0.0 forever.

## Pass isolation sweep

The sweep measures each render feature by turning it off and timing the
difference, so it catches CPU and GPU cost together without needing a GPU
timer query. It runs `profiler.sweep-rounds` rounds (default 5) through every
configuration, `profiler.sweep-frames-per-round` frames each (default 20),
**interleaved** - round 0 measures baseline, lighting off, shadows off, ... in
order, round 1 measures the same list back to front, and so on, instead of
one long contiguous block per configuration.

This matters on hardware with a shared CPU/GPU power budget (an APU is the
common case): a clock/thermal drift over the sweep's several-second runtime
then lands on every configuration roughly equally instead of piling entirely
onto whichever one happened to be measured last - which is what a sequential
block-per-configuration sweep has no way to tell apart from a real
difference, and reads as every feature "costing" a *slower* frame than
baseline, monotonically, in measurement order.

It disables vsync and the fixed timestep while it runs, for the same reason
run 1/2 above do - otherwise every configuration would be pinned to the
refresh rate and read as "no difference". Everything is restored afterwards.

```csharp
GameClient.Sweep.Start();     // then poll GameClient.Sweep.Running
```

Results print in declared order (baseline first), not sorted by cost, so a
result list is directly comparable run to run. Each row also carries a
spread (max − min sample across every round); if the spread is larger than
the measured difference, or a configuration comes out slower than baseline
beyond a small noise floor, the row is flagged instead of presented as a
normal ranked result:

```
configuration                      median      saved     spread      fps
baseline                            4.14ms          -     0.31ms      242
wall bleed off                      3.62ms     0.52ms     0.28ms      276
shadows off                         4.20ms    -0.06ms     0.19ms      238  ⚠ slower than baseline - contamination suspected, don't trust this number
```

## CVars

| CVar | Default | What it does |
|---|---|---|
| `profiler.enabled` | `true` | Master switch for scope timing. |
| `profiler.window` | `120` | Frames kept for the rolling averages. |
| `profiler.gpu-sync` | `false` | Drains the GPU after each GPU-timed pass so its scope measures GPU work. Inflates the frame time on purpose - the numbers are only comparable to each other. |
| `profiler.sweep-frames-per-round` | `20` | Frames measured per configuration, per round, during a sweep. |
| `profiler.sweep-rounds` | `5` | Rounds a sweep cycles through every configuration. |
| `engine.system-profiller-top` | `10` | Systems returned by `SystemsProfiler.GetTop()`. |

With `gpu-sync` on, the first GPU-timed scope of the frame absorbs whatever
the previous frame left queued, so it reads high and should be ignored -
everything after it is that pass' own GPU cost.

## Measure with vsync off

With vsync or the fixed timestep on, the frame time is capped and the report
can only show how the frame is *shared*, not how much headroom is left. To
see the real ceiling:

```csharp
GameClient.Graphics.SynchronizeWithVerticalRetrace = false;
GameClient.Instance.IsFixedTimeStep = false;
GameClient.Graphics.ApplyChanges();
```

The BUILD section always states which of vsync/gpu-sync are on, and VERDICT
explains what that combination means before showing any numbers.
