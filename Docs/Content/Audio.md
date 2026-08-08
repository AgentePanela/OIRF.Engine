# Audio

The engine has an ECS-integrated audio system. Sounds are attached to entities via `AudioComponent`, tagged with any number of runtime-adjustable-volume tags, and can be spatial (distance attenuation + stereo pan against a listener) or flat. Playback itself streams `.ogg` files through [MonoSound](https://github.com/JavidPack/MonoSound), client-only — but the component and its lifecycle logic are shared, so the server can hold and reason about audio state too (see [Client vs. Server](#client-vs-server)).

---

## Quick Start

Attach a looping ambient sound to an entity:

```yaml
- type: audioTag
  id: Ambient

- type: entity
  id: Campfire
  components:
  - type: Transform
  - type: Sprite
    key: campfire
  - type: Audio
    key: SFX/campfire_loop
    volume: 0.6
    loop: true
    spatial: true
    maxDistance: 600
    tags:
      - Ambient
```

Trigger a one-shot sound from code, without needing an owner entity:

```csharp
// inject the concrete type for whichever side this code runs on - see Known Limitations
[Dependency] private readonly Engine.Client.Audio.AudioSystem _audio = default!;

_audio.PlaySound("SFX/gunshot", position: transform.Position, spatial: true, maxDistance: 800);
```

Play a sound tied to an entity you already control, without autoplay:

```yaml
- type: Audio
  key: SFX/alarm
  autoPlay: false
```
```csharp
var comp = EnsureComp<AudioComponent>(uid);
_audio.Play(uid, comp);
```

A one-off sound with no entity involved at all (UI clicks, menu stingers) can skip the ECS entirely:

```csharp
GameClient.Audio.Play("UI/click");
```

---

## Components

### AudioComponent

Attaches a sound to an entity. Lives in `Engine.Shared` (not client-only) so the server's ECS can hold and read this data too — only the client's `AudioSystem` actually plays it back.

| Property | Type | Default | Description |
|---|---|---|---|
| `Key` | `string` | `""` | Relative path (without extension) under the `Audios` resource root, e.g. `"SFX/campfire_loop"` |
| `Volume` | `float` | `1` | Base volume, before Master/tag multipliers and spatial attenuation |
| `Pitch` | `float` | `0` | `-1` (one octave down) to `1` (one octave up) |
| `Loop` | `bool` | `false` | Loop when it reaches the end |
| `AutoPlay` | `bool` | `true` | Start playing the moment this component is added, instead of waiting for a manual `Play()` call |
| `Spatial` | `bool` | `false` | Attenuate volume and pan based on distance from the listener |
| `MaxDistance` | `float` | `1000` | World-units distance at which a spatial sound has faded out completely |
| `Tags` | `HashSet<ProtoId<AudioTagPrototype>>` | empty | Any number of tags (see [Tags](#tags)); each has its own runtime-adjustable volume multiplier |
| `Elapsed` | `float?` | `null` | System-owned — don't set this manually. Seconds since playback started, or `null` when not playing |

### AudioListenerComponent

Marks an entity as the audio listener for spatial audio. Optional — if none exists (or none is `Active`), `AudioSystem` falls back to the `Camera2D` singleton's position.

| Property | Type | Default | Description |
|---|---|---|---|
| `Active` | `bool` | `true` | Only one active listener is used at a time; the first one found wins (no defined tie-break with more than one) |

---

## Tags

Audio tags mirror the engine's [Tag](Tags.md) system: declare a valid tag id as an `audioTag` prototype before using it 0 attaching an undeclared tag throws exception in debug builds.

```yaml
- type: audioTag
  id: Music

- type: audioTag
  id: Ambient

- type: audioTag
  id: Jukebox
```

A sound can carry any number of tags (`AudioComponent.Tags`), unlike a fixed bus enum (Master/Music/SFX/UI) - declare as many as your game needs. Each tag gets its own volume multiplier, adjustable at runtime:

Engine pre-built-in tags:
```yaml
- type: audioTag
  id: UI
```
Used internally by the engines systems and can be also ultilized in your game content.

```csharp
[Dependency] private readonly IAudioManager _audio = default!;

_audio.SetTagVolume("Music", 0.4f);
float current = _audio.GetTagVolume("Music"); // 1 if never set
```

Tag volumes are runtime-only (not persisted to `config.toml`) — reapply them yourself on load if a settings menu needs to remember them. `SharedAudioSystem.GetPlayingByTag(tag)`/`IAudioManager.GetPlayingByTag(tag)` list what's currently playing under a given tag (as entities or `StreamPackage`s, respectively) — useful for e.g. stopping every `Music`-tagged sound at once.

---

## SharedAudioSystem / AudioSystem API

`SharedAudioSystem` (abstract, `Engine.Shared.Audio`) drives `AudioComponent`'s lifecycle and timing — autoplay, looping, `AudioFinishedEvent` — using `SharedAudioManager`'s file metadata, without needing any playback backend. `Engine.Client.Audio.AudioSystem` is the concrete client subclass that layers real MonoSound playback on top; `Engine.Server.Audio.AudioSystem` is an empty subclass that only exists so the shared logic gets registered and ticked server-side too (see [Client vs. Server](#client-vs-server)).

Inject the concrete type for your side — `Engine.Client.Audio.AudioSystem` or `Engine.Server.Audio.AudioSystem` — not the abstract `SharedAudioSystem` itself (see [Known Limitations](#known-limitations), IoC resolves by exact type only):

```csharp
[Dependency] private readonly Engine.Client.Audio.AudioSystem _audio = default!;
```

```csharp
_audio.Play(uid, comp);                 // starts/restarts playback for this entity's AudioComponent
_audio.Stop(uid);                       // stops, no AudioFinishedEvent
_audio.IsPlaying(uid);                  // is Elapsed currently non-null?
_audio.PlaySound("SFX/gunshot", ...);   // throwaway entity, see Quick Start
_audio.GetPlayingByTag(tag);            // entities currently playing with this tag
```

### AudioFinishedEvent

Raised (`SubscribeEvent<AudioComponent, AudioFinishedEvent>`) when a clip reaches the end naturally — not on an explicit `Stop()`, component removal, or restart. A `PlaySound()` entity deletes itself right after this fires.

---

## Volume

Only `Master` is a real engine CVar — tags are open-ended and prototype-defined, not a fixed set, so per-tag volumes are a runtime dictionary instead (see [Tags](#tags)).

| CVar | Default | Description |
|---|---|---|
| `audio.master-volume` | `1.0` | Global multiplier, applied on top of every tag's multiplier |

Effective volume for a playing sound:

```
effective = comp.Volume × Master × Π(tag volume for each tag in comp.Tags) × spatialAttenuation
```

Changing `AudioCvars.MasterVolume` or calling `SetTagVolume` reapplies live to every already-playing stream (a looping `Music` track responds immediately to a settings-menu slider, not just future plays).

---

## Spatial Audio & Listener

For entities with `Spatial: true`, `AudioSystem` (client) recomputes volume/pan every frame:

- **Attenuation** — linear falloff: `1 - distance / MaxDistance`, clamped to `[0, 1]`. Multiplies on top of the tag/Master volume, doesn't replace it.
- **Pan** — `(entityX - listenerX) / MaxDistance`, clamped to `[-1, 1]`.
- **Listener position** — the first entity found with `AudioListenerComponent { Active: true }` and a `TransformComponent`; if none exists, falls back to the `Camera2D` singleton's `WorldCenter`. Most games never need a listener entity — add one only if the listener should track something other than the camera (e.g. the player when the camera lags/leads).

Non-spatial sounds (`Spatial: false`, the default) are unaffected by any of this — they play at a flat volume/pan regardless of position. Use this for UI sounds, global music, and anything else that shouldn't fade with distance.

---


## Client vs. Server

`AudioComponent`, `AudioListenerComponent`, and `AudioTagPrototype` are all `Engine.Shared` — the server's ECS can hold, read, and modify this data on entities without ever touching MonoSound. `SharedAudioManager` (the file/metadata manifest) is also shared and loads identically on both sides during `SharedContentManager.PostInit()`, reading each `.ogg`'s duration/sample rate/channel count straight from its Vorbis container headers (`AudioMetadataReader`) — no decode, no audio device, safe on a headless server.

This means:

- The server can validate a `Key` resolves to a real file (`SharedAudioManager.HasAudio`/`TryGetMetadata`) and knows a clip's exact duration, without playing anything.
- `SharedAudioSystem`'s elapsed-time simulation (used for looping and `AudioFinishedEvent`) runs identically on both sides, off the same metadata — useful for server-authoritative gameplay logic (e.g. an NPC reacting to a `Loud`-tagged sound within range) even though the server never makes a sound.
- Only `Engine.Client.Audio.AudioManager`/`AudioSystem` touch MonoSound and actual playback.

---

## How It Works

Each frame, `SharedAudioSystem.Update`:

1. Advances `Elapsed` for every entity currently playing, using `SharedAudioManager`'s cached `Duration` for that entity's `Key`. Unknown duration (metadata failed to read) just keeps the clock running without ever auto-finishing.
2. When `Elapsed` reaches the clip's duration: wraps back to `0` if `Loop`, otherwise stops the entity and raises `AudioFinishedEvent`.
3. `CompAddedEvent`/`CompRemovedEvent` on `AudioComponent` drive autoplay and stop-on-removal; `OnPlay`/`OnStop` are the extension points a concrete subclass overrides to do (or not do) real playback.

On the client, `AudioSystem.Update` additionally:

4. Reaps any `StreamPackage` MonoSound itself reports finished (a safety net for when the metadata-based estimate can't run, e.g. unreadable headers).
5. For every `Spatial` entity currently playing, recomputes volume/pan against the resolved listener position and pushes it into `AudioManager`.

---

## Known Limitations

- **`SharedAudioSystem` can't be `[Dependency]`-injected directly** — `IoCManager`/`EntityManager.GetSystem<T>()` resolve by exact registered type, and systems are only ever registered under their concrete type (`RegisterSystems()` skips abstract types, so `SharedAudioSystem` itself is never a dictionary key). Content code must inject `Engine.Client.Audio.AudioSystem` or `Engine.Server.Audio.AudioSystem` explicitly, knowing which side it's running on — the shared base is useful for the engine's own client/server split, but doesn't (yet) give content a side-agnostic way to reach it. Registering each concrete system under its abstract ancestors too (in `RegisterSystems()`) would fix this generally for any future `SharedXSystem` split, not just audio.
- Linear-only attenuation falloff — no curve options (compare `PointLightComponent.Falloff` in [Lighting](Lighting.md)).
- No manual pan override for non-spatial sounds — they're always centered.
- Swapping `Key` on a component that's already playing hard-cuts to the new clip; no crossfade.
- Pan doesn't correct for camera/listener rotation — fine for a top-down, no-rotation setup.
- Tag volumes aren't persisted — a settings menu needs to reapply them itself after `SetTagVolume` on load.
- No pooling for rapid repeated `Play()` calls — each open a fresh MonoSound stream; fine for occasional SFX, not for e.g. unthrottled rapid-fire gunfire.
