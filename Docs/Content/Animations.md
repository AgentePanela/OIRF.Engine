# Animations

The engine supports frame-based sprite animations declared in an `info.yml` file placed inside a
texture folder. The engine slices/queues the frames into the atlas at load time, and a small
ECS component + system pair drives playback on entities.

This page assumes you already know the texture/atlas basics from [Resources](Resources.md) and
the `SpriteComponent`/`SpriteSystem` basics from [Graphics](Graphics.md).

---

## info.yml

Drop an `info.yml` next to the textures it describes, e.g. `Resources/Textures/Player/info.yml`.
It lists every animation defined for that folder under `animations:`.

```yaml
animations:
  - id: walk-anim
    spritesheet: true
    files: walk            # single file name, no extension
    frameWidth: 32
    frameHeight: 32
    frameCount: 8
    speed: 12
    loop: true
    frameSpeeds: [0.5, 1, 1, 1, 1, 1, 1, 2]   # optional per-frame duration multiplier

  - id: idle-anim
    spritesheet: false
    files: [idle-1, idle-2, idle-3]   # one file per frame, in order, no extension
    speed: 4
    loop: true
```

### Fields

| Field | Required | Default | Description |
|---|---|---|---|
| `id` | yes | — | Animation id. Combined with the folder to form the animation key, e.g. `Player/walk-anim`. |
| `files` | yes | — | A single file name (no extension) when `spritesheet: true`, or a list of file names (no extension) when `false`. |
| `spritesheet` | no | `false` | Whether `files` points at one sheet to slice, or a list of standalone frame files. |
| `frameWidth` / `frameHeight` | only if `spritesheet: true` | — | Pixel size of a single cell in the sheet. |
| `frameCount` | only if `spritesheet: true` | — | How many cells to read out of the sheet (left-to-right, top-to-bottom). Implicit (`files.Length`) when `spritesheet: false`. |
| `speed` | no | `10` | Default frames per second. |
| `loop` | no | `true` | Whether the animation restarts after the last frame. If `false`, playback stops (`AnimationComponent.Playing = false`) on the last frame. |
| `frameSpeeds` | no | — | Optional array of per-frame duration multipliers, index-aligned with the frame number. A value of `2` makes that frame stay twice as long as `1/speed`. |

### Spritesheet vs. separate files

Either authoring style produces identical results at runtime — pick whichever your art pipeline
makes easier:

- **`spritesheet: true`** — one image (`walk.png`) is decoded once at load time and cut into a
  grid of `frameWidth × frameHeight` cells. Good for tightly packed animations exported as a
  single sheet.
- **`spritesheet: false`** — each frame is its own PNG. No slicing happens; each file is queued
  into the atlas directly under its animation-frame key. Good when frames come out of your art
  tool as separate exports.

---

## How frames are keyed in the atlas

Every frame ends up in the atlas under `{folder}/{id}-{frameIndex}`, e.g. `Player/walk-anim-0`,
`Player/walk-anim-1`, etc. — these are normal atlas keys, usable anywhere a sprite key is
expected (`SpriteComponent.Key`, `IAssetManager.GetSprite`, ...).

The **bare** key (`Player/walk-anim`, no frame suffix) is *not* an atlas entry. It only exists in
an internal animation registry (frame count, speed, loop) used by `AnimationSystem` and by a
static fallback in `IAssetManager.GetSprite` — referencing the bare key without an
`AnimationComponent` resolves to frame `0`, so it's safe to use directly as a static preview
(e.g. an inventory icon).

---

## AnimationComponent + AnimationSystem

Add both `Sprite` and `Animation` to an entity prototype. `Animation.key` is independent from
`Sprite.key` — `AnimationSystem` overwrites `SpriteComponent.Key` every time the frame advances.

```yaml
- type: entity
  id: Player
  components:
  - type: Sprite
    key: Player/walk-anim   # initial/fallback frame (resolves to frame 0 until animated)
  - type: Animation
    key: Player/walk-anim
```

```csharp
[RegisterComponent("Animation")]
public class AnimationComponent : Component
{
    public string Key { get; set; } = string.Empty; // animation key, e.g. "Player/walk-anim"
    public bool Playing { get; set; } = true;

    public float? SpeedOverride { get; set; } = null;  // null = use info.yml's speed (frames per second)
    public bool? LoopOverride { get; set; } = null;    // null = use info.yml's loop value

    // runtime only — do not set manually
    public int CurrentFrame { get; set; }
    public float Elapsed { get; set; }
}
```

Each frame, `AnimationSystem`:

1. Skips the entity if `Playing` is `false` or the key isn't a known animation.
2. Accumulates `dt` into `Elapsed`.
3. Once `Elapsed` passes the current frame's duration (`1/speed`, scaled by `frameSpeeds[frame]`
   if present), advances `CurrentFrame` — wrapping to `0` if `loop`, or stopping on the last
   frame (`Playing = false`) otherwise.
4. Writes `SpriteComponent.Key = "{animation key}-{CurrentFrame}"`.

No changes are needed in `SpriteSystem` for this to render correctly — it already re-resolves its
cached `Sprite2D` whenever `SpriteComponent.Key` changes between frames.

### Controlling playback

Prefer the `AnimationSystem` API over poking at `AnimationComponent` fields directly — it keeps
`SpriteComponent.Key` in sync and raises the lifecycle events below.

```csharp
[Dependency] private readonly AnimationSystem _animSys = default!;

// Switch to a different animation, restarting from frame 0. Adds AnimationComponent if missing.
_animSys.SetAnimation(uid, "Player/attack-anim");

// Read back the currently assigned animation's definition (frame count, speed, loop)
AnimationDef? def = _animSys.GetAnimation(uid);

// Pause / resume without losing the current frame
_animSys.Pause(uid);
_animSys.Resume(uid);

// Per-entity overrides — don't mutate the shared info.yml-loaded AnimationDef
_animSys.SetSpeed(uid, 20f);    // play at a fixed 20 fps, regardless of info.yml's speed
_animSys.SetSpeed(uid, null);   // go back to whatever info.yml says
_animSys.SetLoop(uid, false);   // force this entity's instance to stop after one pass
_animSys.SetLoop(uid, null);    // go back to whatever info.yml says

// Clears SpeedOverride/LoopOverride and resets CurrentFrame to 0
_animSys.Reset(uid);
```

### Events

`AnimationSystem` raises entity events you can subscribe to (see [ECS](Ecs.md#event-bus)):

| Event | Fires when |
|---|---|
| `AnimationStartedEvent` | `SetAnimation` assigns a (new) animation. |
| `AnimationFrameChangedEvent` | Playback advances to a new frame. Has a `Frame` field — useful for syncing hitboxes, footstep sounds, etc. to specific frames. |
| `AnimationLoopedEvent` | A looping animation wraps back to frame 0. |
| `AnimationFinishedEvent` | A non-looping animation reaches its last frame; `AnimationComponent.Playing` is set to `false`. |

```csharp
SubscribeEvent<AnimationComponent, AnimationFinishedEvent>(OnAttackFinished);

void OnAttackFinished(EntityUid uid, AnimationComponent anim, AnimationFinishedEvent ev)
{
    _animSys.SetAnimation(uid, "Player/idle-anim");
}
```

---

## API reference

```csharp
// IAssetManager
public bool TryGetAnimation(string key, out AnimationDef? def);

// AnimationSystem
public bool SetAnimation(EntityUid uid, string key);   // switches animation, resets to frame 0
public AnimationDef? GetAnimation(EntityUid uid);
public void Pause(EntityUid uid);
public void Resume(EntityUid uid);
public void SetSpeed(EntityUid uid, float? speed);  // per-entity override (fps), null = use info.yml
public void SetLoop(EntityUid uid, bool? loop);      // per-entity override, null = use info.yml
public void Reset(EntityUid uid);                    // clears overrides, resets to frame 0
```

`AnimationDef`:

| Property | Type | Description |
|---|---|---|
| `Key` | `string` | Full animation key, e.g. `Player/walk-anim`. |
| `FrameCount` | `int` | Total number of frames. |
| `Speed` | `float` | Default frames per second. |
| `Loop` | `bool` | Whether playback loops. |
| `FrameSpeeds` | `float[]?` | Optional per-frame duration multipliers. |
| `GetFrameDuration(int frame)` | `float` | Seconds the given frame should be displayed for. |
| `FrameKey(int frame)` | `string` | Atlas key for that frame, e.g. `Player/walk-anim-3`. |
