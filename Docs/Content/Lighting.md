# Lighting

Lights are components on entities. Every frame the engine draws them, with their shadows, into a **light map** per viewport, and every sprite, tile and text in the world is multiplied by the light map where it stands.

- Lights: `PointLight` (round, cone or textured), `SpriteLight` (a sprite layer that glows), `AmbientLight` (the base light).
- Shadows: `Occluder`. An occluder can be glass, tinting the light that goes through it.
- On top of that: the field of view, bloom, and hooks for the content (animating lights, drawing into the light map).

---

## Quick start

Lighting is off until the game turns it on:

```csharp
[Dependency] private readonly LightingManager _lighting = default!;

_lighting.SetEnabled(true);
```

A light, and a wall that casts a shadow:

```yaml
- type: entity
  id: Torch
  components:
  - type: Transform
  - type: Sprite
    key: props/torch
  - type: PointLight
    color: [255, 200, 140, 255]
    radius: 196
    intensity: 1.2

- type: entity
  id: Wall
  components:
  - type: Transform
  - type: Sprite
    key: walls/stone
  - type: Occluder
    shape: Rectangle
    size: [32, 32]
```

Where no light reaches, things are drawn in the ambient color (black by default; see [Ambient light](#ambientlight)).

---

## PointLight

| Field | Default | Description |
|---|---|---|
| `enabled` | `true` | |
| `color` | white | |
| `radius` | `256` | How far it reaches, in world units |
| `intensity` | `1` | Brightness. Can go over 1, see `light.overbright` |
| `castShadows` | `true` | False goes through occluders |
| `offset` | `[0, 0]` | From the entity position. Not rotated with the entity |
| `falloff` | `Quadratic` | `Linear`, `Quadratic` or `InverseSquare` |
| `softness` | `1` | How big the light source is: the width of its shadow edges. `0` gives hard shadows |
| `mask` | none | A texture key whose alpha shapes the light, stretched over the whole light |
| `rotation` | `0` | Turns the cone and the mask, in radians |
| `rotatesWithTransform` | `true` | Adds the entity rotation to `rotation` |
| `coneAngle` | `360` | Full width of the cone, in degrees |
| `coneSoftness` | `0.25` | How much of the cone fades at its edges: 0 hard, 1 from the middle |

A flashlight is a cone that turns with its entity:

```yaml
  - type: PointLight
    radius: 320
    coneAngle: 50
    softness: 0.5
```

A light that is shaped by a texture (a window frame, a grate) uses `mask`; the texture is stretched over the square of `radius * 2` around the light.

---

## AmbientLight

The light everywhere no other light reaches. With several, the one with the highest `priority` is used; with none, `LightingManager.AmbientLight`.

| Field | Default |
|---|---|
| `color` | `[40, 40, 50]` |
| `intensity` | `0.2` |
| `priority` | `0` |

The content can also add light by area (a lit room, a dark corridor) by drawing into the light map, see [Drawing into the light map](#drawing-into-the-light-map).

---

## Occluder

Stops the light of every `castShadows` light near it.

| Field | Default | Description |
|---|---|---|
| `enabled` | `true` | Off for an open door |
| `shape` | `Rectangle` | `Rectangle`, `Circle` (a 12 sided polygon) or `Sprite` (the box of the sprite) |
| `size` | `[64, 64]` | For `Rectangle` |
| `radius` | `32` | For `Circle` |
| `offset` | `[0, 0]` | From the entity position |
| `opacity` | `1` | How much of the light it stops, 0 to 1 |
| `tint` | white | The color of the light that goes through it |
| `fovHeight` | `0` | For the field of view only, see [below](#tall-occluders) |

The occluder casts shadow from its outline only: its own area stays lit, and neighbouring occluders that touch merge into one outline, so a row of walls has no shadow seams between them.

A light whose center is inside an occluder makes odd shadows. Keep wall lamps a little off the wall (`offset`); `light.occluder-overlay` circles the lights that are inside one.

### Glass

An occluder with `opacity` under 1 lets light through, tinted: what passes is `tint * (1 - opacity)`.

```yaml
  - type: Occluder
    size: [32, 32]
    opacity: 0.3
    tint: "#ff6040"     # orange stained glass
```

Glass does not hide anything from the field of view.

---

## SpriteLight

Makes layers of the sprite light what is around them, in the color of what they show (a screen, a sign, a lava tile). It follows the animation of the layer: a screen that changes picture changes the color of its light.

```yaml
  - type: Sprite
    layers:
    - id: Screen
      key: props/monitor_screen
      unshaded: true
  - type: SpriteLight
    lights:
    - layer: Screen
      spread: 48
      offset: [0, 24]
```

| Entry field | Default | Description |
|---|---|---|
| `layer` | required | The id of the sprite layer. A layer that does not exist is logged once |
| `color` | white | Multiplies the color of the layer |
| `intensity` | `1` | |
| `spread` | `32` | How far past the edges of the layer the light reaches, fading out |
| `offset` | `[0, 0]` | Moves the light away from the layer, to throw it on the floor in front of a wall screen |

A sprite light is the average color of the frame on a soft rectangle. It casts no shadows. After changing an entry from code, call `Dirty()` on the component.

---

## How sprites take the light

Each sprite (and each layer) reads the light map at one place per pixel. These sprite and layer fields choose where:

| Field | Default | Description |
|---|---|---|
| `unshaded` | `false` | Ignores the light, drawn at full brightness |
| `emission` | `0` | 0 to 1, how much it ignores the light: in between, it is never darker than that |
| `lightBase` | `Pixel` | Where it reads the light, below |
| `lightOffset` | `[0, 0]` | The base line or point, from the entity position |

| `lightBase` | Reads the light |
|---|---|
| `Pixel` | Under each pixel. Right for anything flat on the floor |
| `Line` | On the base line (the entity position plus `lightOffset`), straight below or above each pixel. Made for tall sprites seen in 3/4, like the face of a wall: the whole face takes the light of the floor in front of it, not of whatever is behind it |
| `Point` | At the base point, for the whole sprite |
| `PixelCapped` | Under each pixel, but never above the base line. For characters: their head does not light up from a lamp on the far side of a wall |

Layers that do not set them use the sprite's. A shader prototype with `lit: false` ignores the light too (see [Shaders](Shaders.md#lighting)).

---

## Field of view

A viewport with an eye hides what the eye cannot see, using the same occluders as the light: everything out of sight is painted over with `fov.color`.

```csharp
_renderer.MainViewport.Eye = playerUid;
```

| `RenderViewport` | Default | Description |
|---|---|---|
| `Eye` | none | The entity it sees from. No eye, no field of view |
| `EyeOffset` | `[0, 0]` | From the eye's position |
| `FovEnabled` | `true` | |
| `FovRadius` | `0` | How far it sees, 0 for no limit |
| `FovRadiusSoftness` | `32` | How wide the edge of the radius fades |

The `togglefov` console command flips `FovEnabled` on the main viewport. The field of view does not need the lighting to be on.

It is only drawn: the entities out of sight are still there, sent by the server and clickable.

### Tall occluders

The field of view is cut at the outline of the occluders on the floor. A wall drawn taller than its outline (3/4 perspective) would have the top of its sprite cut. `fovHeight` raises the occluder for the field of view only, by that many world units upwards; the light keeps using the outline on the floor.

- In a column of occluders, only the top one rises.
- It does not rise when the eye is less than `fovHeight` north of it, so a corridor right behind a wall is still visible.

### Reading the mask

`RenderViewport.FovMask` is what the eye did not see last frame (alpha 1 is hidden), ready to submit over the world from a [render overlay](Graphics.md#render-overlays). It is how the content can do its own treatment of what is out of sight: turn off the engine's (`FovEnabled = false`) and draw from the mask.

---

## Animating lights

The engine has no light animation of its own; it gives the hook. Every frame, before a `PointLight` is drawn, `LightDrawingEvent` is raised on its entity with the values it is about to be drawn with. A handler changes them; the component is not touched, so nothing is sent over the network.

```csharp
public override void Init()
{
    base.Init();
    SubscribeEvent<CandleComponent, LightDrawingEvent>(OnLightDrawing);
}

private void OnLightDrawing(EntityUid uid, CandleComponent comp, LightDrawingEvent args)
{
    args.Intensity *= 0.8f + 0.2f * MathF.Sin(_time * 12f);
    args.Animated = true;
}
```

| Field | |
|---|---|
| `Enabled`, `Color`, `Radius`, `Intensity` | What the light is drawn with |
| `Center` | World position of the light, with its `offset`. Move it to draw the light somewhere else than the entity (a lamp drawn on a wall that lights the floor in front of it) |
| `Rotation` | Radians, added to the rotation of the light |
| `Animated` | Set it when the values change every frame: the light is not kept in the cache |

The same event instance is reused for every light: do not keep it.

---

## Drawing into the light map

A [render overlay](Graphics.md#render-overlays) at `RenderStage.LightMap` draws into the light map of each viewport, in world space and adding up like the lights. Use it for light by area, or anything the light components do not cover.

`RenderViewport.LightFrame` is the light map of the last frame, ready to submit, and `LightingEnabled` turns the light off for one viewport.

---

## Bloom

`light.bloom 1` makes what is brighter than `light.bloom-threshold` glow, in every viewport drawn with light. It runs after the field of view and before the `AfterLighting` stage. `RenderViewport.BloomEnabled` keeps one viewport out.

---

## LightingManager

```csharp
[Dependency] private readonly LightingManager _lighting = default!;

_lighting.SetEnabled(true);              // off by default
_lighting.SetAmbient(Color.Black, 0.1f); // when there is no AmbientLight
_lighting.LightIntensity = 1.2f;         // multiplies every light
```

`OnEnabledChanged` fires when `Enabled` changes.

---

## Settings (CVars)

| CVar | Default | Description |
|---|---|---|
| `light.resolution` | `0.5` | Light map pixels per world unit |
| `light.pixelated` | `false` | Light in blocks instead of smooth |
| `light.pixel-size` | `1` | Size of those blocks, in world units |
| `light.overbright` | `2` | How much brighter than drawn the light can make something |
| `light.dither` | `true` | Hides the banding of the light |
| `light.margin` | `128` | How far past the view the light map reaches |
| `light.max-size` | `4096` | Largest light map (and field of view mask), in pixels |
| `light.softness-scale` | `8` | Multiplies every `softness` |
| `light.cache` | `true` | Keeps the light of what does not change. Off only to compare |
| `light.static-delay` | `0.5` | Seconds a light has to stay the same before it is kept |
| `light.bloom` | `false` | |
| `light.bloom-threshold` | `0.8` | 0 to 1, the brightness the glow starts at |
| `light.bloom-intensity` | `0.5` | |
| `fov.color` | `#000000FF` | What hides what the eye does not see. A translucent color darkens instead |

## Debugging

- The profiler's LIGHTING panel: lights (static, dynamic, animated), sprite lights, shadow edges, field of view, bloom, chunks, atlas pages and their memory, and the time of each part.
- The debug window's **Lighting** tab: the atlas pages and the chunk pages, and the toggles below.
- `light.occluder-overlay`: the occluder outlines (merged edges in grey) and the lights inside an occluder.
- `light.debug-base`: replaces the light with colored stripes by world position, so each sprite shows the color of where it reads the light. A `Line` wall face comes out in one color, a `Pixel` sprite striped. A white dot marks the base of every sprite that is not `Pixel`.

---

## Performance

- A light that has not changed for `light.static-delay` seconds is kept: its light and shadows are drawn once into an atlas and summed into chunks of the world. Standing still, the lighting costs almost nothing however many lights there are.
- A light that moves or changes (or that a handler marks `Animated`) is drawn every frame, with its shadows. Changing an occluder redraws the lights that reach it.
- All lights of a frame are drawn in a handful of draw calls, whatever their number.
- Lights inside a container (held, in a bag) are off.
