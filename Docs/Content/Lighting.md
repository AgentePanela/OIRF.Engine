# Lighting

The engine has a built-in 2D dynamic lighting system. Lights are added to entities via components, occluders block light to cast shadows, and the engine composites everything into a per-frame lightmap that's multiplied over the rendered scene.

---

## Quick Start

Add a light to an entity prototype:

```yaml
components:
  - type: Transform
  - type: Sprite
    key: torch
  - type: PointLight
    color: [255, 200, 140, 255]
    radius: 196
    intensity: 1.2
    castShadows: true
```

Add a cone-shaped spotlight:

```yaml
components:
  - type: Transform
  - type: SpotLight
    color: [200, 220, 255, 255]
    radius: 320
    intensity: 1.5
    coneAngle: 60
    direction: 0
    rotatesWithTransform: true
    castShadows: true
```

`direction` is relative to the entity's own rotation when `rotatesWithTransform` is `true` — rotate the entity (or its `Transform.Angle`) at runtime to swing the cone around. Set `rotatesWithTransform: false` to use `direction` as an absolute world angle instead.

Mark a wall or obstacle as a shadow caster:

```yaml
components:
  - type: Transform
  - type: Sprite
    key: wall
  - type: Occluder
    shape: Sprite
```

Any `PointLight` or `SpotLight` with `castShadows: true` will now cast a shadow around that entity.

---

## Components

### PointLightComponent

Radial light emitted from the owning entity's transform.

| Property | Type | Default | Description |
|---|---|---|---|
| `Color` | `Color` | `White` | Light color |
| `Radius` | `float` | `256` | Falloff distance in world units |
| `Intensity` | `float` | `1` | Brightness multiplier |
| `CastShadows` | `bool` | `true` | Occluded by world geometry |
| `Offset` | `Vector2` | `(0,0)` | Local offset from the transform |
| `Falloff` | `FalloffMode` | `Quadratic` | `Linear`, `Quadratic`, or `InverseSquare` |
| `Softness` | `float` | `1.0` | Shadow PCF kernel multiplier; `0` = hard edge |

### SpotLightComponent

A true cone light: radiates like `PointLight` but restricted to an angular cone around `Direction`, with its own soft-edged falloff and shadow casting.

| Property | Type | Default | Description |
|---|---|---|---|
| `Color` | `Color` | `White` | Light color |
| `Radius` | `float` | `256` | Falloff distance in world units |
| `Intensity` | `float` | `1` | Brightness multiplier |
| `CastShadows` | `bool` | `true` | Occluded by world geometry |
| `Offset` | `Vector2` | `(0,0)` | Local offset from the transform |
| `Falloff` | `FalloffMode` | `Quadratic` | `Linear`, `Quadratic`, or `InverseSquare` |
| `Softness` | `float` | `1.0` | Shadow PCF kernel multiplier; `0` = hard edge |
| `Direction` | `float` | `0` | Cone center angle, in radians (`0` = +X) |
| `ConeAngle` | `float` | `45` | Full angular width of the cone, in degrees. `360` behaves like a point light |
| `RotatesWithTransform` | `bool` | `true` | When true, `Direction` is added to the entity's transform angle, so rotating the entity swings the cone |

### TextureLightComponent

A light whose shape comes from a texture instead of a radial disc — spotlights, glowing windows, sign panels, custom cones.

| Property | Type | Default | Description |
|---|---|---|---|
| `Texture` | `string` | `""` | Asset key for the light shape (e.g. `"Lights/SpotlightCone"`) |
| `Color` | `Color` | `White` | Light color |
| `Intensity` | `float` | `1` | Brightness multiplier |
| `Scale` | `Vector2` | `(1,1)` | Local scale of the texture |
| `Offset` | `Vector2` | `(0,0)` | Local offset from the transform |
| `RotatesWithTransform` | `bool` | `true` | Follows entity rotation |
| `Rotation` | `float` | `0` | Extra rotation in radians, added on top of the entity rotation when `RotatesWithTransform` is true, or used as the light's absolute rotation when it's false |
| `CastShadows` | `bool` | `false` | Reserved for future use — currently has no effect (see [Known Limitations](#known-limitations)) |

### AmbientLightComponent

Sets the base fill color/intensity applied where no other light reaches. If multiple are present in a scene, the one with the highest `Priority` wins.

| Property | Type | Default | Description |
|---|---|---|---|
| `Color` | `Color` | `(40, 40, 50)` | Ambient fill color |
| `Intensity` | `float` | `0.2` | Ambient brightness |
| `Priority` | `int` | `0` | Tiebreaker between multiple ambient lights |

If no `AmbientLightComponent` exists in the scene, `LightingManager.AmbientLight` is used instead.

### OccluderComponent

Marks an entity as a shadow caster. Any shadow-casting light near it will have its light blocked by the occluder's silhouette.

| Property | Type | Default | Description |
|---|---|---|---|
| `Shape` | `OccluderShape` | `Sprite` | `Sprite` (uses sprite bounds), `Circle`, or `Rectangle` |
| `Radius` | `float` | `32` | Used when `Shape = Circle` |
| `Size` | `Vector2` | `(64, 64)` | Used when `Shape = Rectangle` |

---

## LightingManager

`LightingManager` is the runtime config service for the whole lighting pipeline. Resolve it via IoC:

```csharp
[Dependency] private readonly LightingManager _lighting = default!;
```

Common settings:

```csharp
_lighting.SetEnabled(false);                 // master on/off toggle
_lighting.SetAmbient(Color.Black, 0.1f);      // override scene ambient
_lighting.LightIntensity = 1.2f;              // global brightness multiplier
_lighting.MaxLights = 64;                     // hard cap, sorted by distance to camera
_lighting.MaxShadowcastingLights = 16;        // shadow-map row budget
_lighting.HardShadows = true;                 // cheap single-sample shadows
_lighting.WallBleedEnabled = true;            // walls pick up the glow of nearby lights
_lighting.LightBlurEnabled = true;            // blur final lightmap
```

When `Enabled` is `false`, the lighting system does no work in Update or Draw and the scene renders with raw sprite colors — zero overhead.

`LightingManager` also exposes per-frame stats (`LastVisibleLights`, `LastShadowLights`, `LastOccluders`, `LastLightingTotalMs`, etc.) used by the in-engine debug overlay and the **Lighting** tab of the debug window.

### CVars

| CVar | Default | Description |
|---|---|---|
| `lighting.lightmap-scale` | `1.0` | Fraction of viewport resolution used for the lightmap (lower = cheaper, blurrier) |
| `lighting.pixelated` | `false` | Snap the lightmap to a low-res pixel grid |
| `lighting.pixel-size` | `8` | Screen pixels per lightmap texel when pixelated mode is on |

---

## Unshaded Sprites

Sprites are lit by default. To exclude a sprite from lighting entirely (UI elements drawn in world space, fullbright VFX, etc.), assign it the `Unshaded` shader:

```yaml
components:
  - type: Sprite
    key: glow_fx
    shader: Unshaded
```

Anything using the `Unshaded` technique draws at full brightness, bypassing the lightmap - but its `Layer`/`Depth` is still respected normally against every other sprite and shape. An unshaded sprite on a lower `Layer` than a lit sprite gets occluded by it like any other sprite would; it isn't unconditionally drawn on top.

---

## How It Works

Each frame, `LightingSystem`:

1. Collects visible `PointLight`/`SpotLight` entities and all `Occluder` entities near the view (padded by the largest light radius, so off-screen walls still cast shadows into view).
2. Builds the occluder edge geometry once and renders a 1D cylindrical shadow map (one row per shadow-casting light — point and spot share the same map and row budget).
3. Draws every light additively into a lightmap render target, sampling the shadow map for occlusion. Spotlights additionally discard pixels outside their cone. Texture lights are drawn on top with plain additive sprites.
4. Wall bleed (optional): blurs the lightmap at half resolution, then draws the occluder quads over the lightmap so each wall pixel shows the blurred glow of nearby lights.
5. Light blur (optional): a final 2-pass separable Gaussian over the lightmap, smoothing shadow banding.
6. Draws every queued sprite/shape in one pass, sorted by `Layer`/`Depth`, into an offscreen scene target — stamping a stencil bit per pixel (0 = shaded, 1 = `Unshaded`) as it goes. Multiplies the lightmap onto that scene in place, gated by the stencil test so only shaded (0) pixels are affected, then blits the result to the backbuffer.

---

## Known Limitations

- `TextureLightComponent.CastShadows` is not yet wired up — texture lights never occlude and are always drawn unoccluded regardless of the field's value. (`PointLight` and `SpotLight` both cast shadows correctly.)
- Shadows use standard PCF, not true variance shadow mapping — soft shadow quality is driven by `Softness` / `LightSoftness`, not VSM probability falloff.
- Occluder bounds are axis-aligned and ignore entity rotation/scale.
