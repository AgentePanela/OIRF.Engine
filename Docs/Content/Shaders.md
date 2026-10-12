# Shaders

A shader has two halves:

1. A **`.fx` file** under `Resources/Shaders/`: the HLSL code.
2. A **`shader` prototype** in YAML: the id sprites refer to, how it is drawn, and the values it starts with.

Sprites and tilemaps never name a `.fx` file, they name a shader prototype.

The engine ships no shaders of its own, only the header every shader includes (`Sprite.fxh`). Every shader named in this document (`Grayscale`, `Tint`, `Outline`, `Dissolve`...) is an example of one you would write.

```
Resources/
  Shaders/
    Grayscale.fx              <- the code
  Prototypes/
    shaders.yml               <- - type: shader, id: Grayscale
```

```yaml
components:
  - type: Sprite
    key: player/idle
    shader: Grayscale         # the prototype id
```

---

## Quick start

`Resources/Shaders/Tint.fx`:

```hlsl
#include "Sprite.fxh"

float3 TintColor = float3(1.0, 0.5, 0.5);

float4 MainPS(SpritePixel input) : COLOR
{
    float4 color = SampleSprite(input);
    color.rgb *= TintColor;
    return color;
}

SPRITE_TECHNIQUE(MainPS)
```

`Resources/Prototypes/shaders.yml`:

```yaml
- type: shader
  id: Tint
```

That is a complete shader. Three rules:

- **Always start with `#include "Sprite.fxh"`.** It is the engine's header: it declares the texture, the vertex shader and everything below. Do not declare `SpriteTexture`, a sampler, a vertex shader or the `#if OPENGL` block yourself.
- **The pixel shader takes a `SpritePixel`** and returns the color.
- **Always end with `SPRITE_TECHNIQUE(YourPixelShader)`.** Do not write a `technique` block by hand: the sprite would be drawn without the engine's vertex shader, and `LocalUV` and the custom values would be garbage.

Shaders are compiled when the game loads (see [Building](#building)). When one does not compile, the log says so and everything using it is drawn with no shader.

---

## What the header gives you

### `SpritePixel`

| Field | What it is |
|---|---|
| `Color` | The tint of the sprite (`SpriteComponent.Color`, a layer's color...) |
| `UV` | Where to sample the texture. Sprites share atlas pages, so this is **not** 0..1 over the sprite |
| `LocalUV` | 0..1 over the sprite itself: `(0, 0)` is its top left, `(1, 1)` its bottom right. Use this for anything positioned on the sprite (a line across it, a gradient, a cut) |
| `Custom0`, `Custom1` | Two `float4` each sprite carries on its own. See [per sprite parameters](#3-per-sprite-the-cheap-way) |

A flipped sprite has its `LocalUV` flipped too: it follows the texture.

### Functions and textures

| Name | What it is |
|---|---|
| `SampleSprite(input)` | The sprite as it would be drawn with no shader: texture times tint |
| `SpriteTextureSampler` | The texture being drawn, for sampling somewhere else than `input.UV` (`tex2D(SpriteTextureSampler, uv)`) |

### Values the engine fills in

Declared by the header, set by the renderer. Just use them; never set them from a prototype or from code.

| Name | Type | What it is |
|---|---|---|
| `TexelSize` | `float2` | The size of one texel of the texture being drawn. `uv + float2(TexelSize.x, 0)` is the pixel to the right |
| `Time` | `float` | Seconds since the game started |
| `ScreenSize` | `float2` | The size in pixels of the target being drawn into |
| `MatrixTransform` | `float4x4` | Used by the vertex shader |

---

## The shader prototype

```yaml
- type: shader
  id: Dissolve            # what sprites put in `shader:`
  path: Dissolve          # the .fx file name, without extension. Defaults to the id
  lit: true               # false: ignores the lighting (screens, glows, debug)
  blend: Alpha            # Alpha | Additive | Multiply | Opaque
  params:                 # starting values
    EdgeWidth: 0.05
  perSprite:              # parameters that travel with each sprite
    Amount: custom0.x
    EdgeColor: custom1
```

| Field | Default | Description |
|---|---|---|
| `id` | required | The name sprites and tilemaps refer to |
| `path` | the id | `.fx` file name under `Shaders/`, without extension |
| `lit` | `true` | `false` draws at full brightness, untouched by the lighting |
| `blend` | `Alpha` | `Alpha` (regular transparency), `Additive` (adds, for glows), `Multiply` (darkens), `Opaque` (replaces) |
| `params` | none | Starting value for each parameter: a number, or a list of up to 4 numbers for a vector or color |
| `perSprite` | none | Which parameters travel with the sprite, and where. See below |

Several prototypes can point at the same `.fx`, each with its own `lit`, `blend` and starting values:

```yaml
- type: shader
  id: GlowRed
  path: Glow
  blend: Additive
  lit: false
  params:
    GlowColor: [1, 0.2, 0.2, 1]

- type: shader
  id: GlowBlue
  path: Glow
  blend: Additive
  lit: false
  params:
    GlowColor: [0.2, 0.4, 1, 1]
```

---

## Parameters

There are three places a parameter value can come from. Which one to use depends on **who the value is different for**.

### 1. The same for everything using the shader: `params`

Declare it in the `.fx` and give it a value in the prototype. The default written in the `.fx` is used when the prototype says nothing.

```hlsl
float LineCount = 64.0;
```

```yaml
- type: shader
  id: ScanLines
  params:
    LineCount: 32
```

This costs nothing: every sprite with this shader is drawn together.

### 2. Changed at runtime for everything: the shared instance

```csharp
[Dependency] private readonly ShaderManager _shaders = default!;

_shaders.GetSharedInstance("ScanLines")?.SetParameter("LineCount", 128f);
```

`GetSharedInstance` returns the one `ShaderInstance` every sprite with that shader shares, so this changes all of them. Still drawn together. `GetUniqueInstance` gives a copy with parameters of its own instead (dispose it when done); what is drawn with a copy is drawn apart from the rest.

### 3. Per sprite, the cheap way

For a value that is different on each sprite (how far it has dissolved, the color of its outline), **do not** declare a parameter in the `.fx`. List it under `perSprite` in the prototype and read it from `Custom0` / `Custom1` in the shader:

```yaml
- type: shader
  id: Outline
  perSprite:
    DrawColor: custom0          # all four components
  params:
    DrawColor: [1, 1, 1, 1]     # what a sprite gets when nobody set it
```

```hlsl
float4 MainPS(SpritePixel input) : COLOR
{
    float4 drawColor = input.Custom0;
    ...
}
```

```csharp
[Dependency] private readonly SpriteSystem _sprite = default!;

_sprite.SetShaderParam(uid, null, "DrawColor", Color.Red);       // the base sprite
_sprite.SetShaderParam(uid, "face", "Amount", 0.4f);             // one layer
```

The value is written into the vertices of that sprite. Sprites with different values are **still drawn in the same draw call**, which is the whole point: a hundred sprites each at a different point of a dissolve are one draw call, not a hundred.

Where a parameter goes:

| `perSprite` value | Takes | Read in the shader as |
|---|---|---|
| `custom0` | 4 floats | `input.Custom0` |
| `custom1` | 4 floats | `input.Custom1` |
| `custom0.x`, `custom1.w`... | 1 float | `input.Custom0.x`... |
| `custom0.xy`, `custom1.zw`... | 2 floats | `input.Custom0.xy`... |
| `custom0.xyz` | 3 floats | `input.Custom0.xyz` |

Components have to follow each other (`xy`, `yzw`; not `xz`), and it is up to you not to put two parameters on the same components.

**The limit is 8 floats per sprite** (two `float4`). A shader that needs more per sprite has to send the rest the expensive way.

### 4. Per sprite, the expensive way

`SetShaderParam` with a name that is **not** under `perSprite` but is a parameter of the `.fx` still works: the sprite gets its own copy of the shader with that value. A sprite with its own copy cannot be drawn together with the others, so it costs a draw call of its own. Fine for one or two things on screen (the player under an effect), wrong for something a whole room of sprites does.

The profiler's RENDERING panel shows how many copies exist (`Shaders: N shared | N owned`) and how many batches were split by a shader change (`Breaks: ... shader`).

### How `SetShaderParam` behaves

- The value is kept **by name**, per sprite (or per layer). It applies to whatever shader the sprite is drawn with that knows the name, and is ignored by one that does not.
- It survives the shader being swapped and swapped back (`SetDrawShader`), and can be set before the shader is.
- Layers are separate: setting it on the base sprite (`layerId: null`) does not set it on its layers.

### Summary

| The value is... | Put it in | Cost |
|---|---|---|
| Fixed for the shader | `params` | none |
| Changing, same for every sprite | `GetSharedInstance(id).SetParameter` | none |
| Different per sprite, up to 8 floats | `perSprite` + `SetShaderParam` | none |
| Different per sprite, beyond that | `SetShaderParam` on a `.fx` parameter | one draw call per sprite |

---

## Full example: a dissolve, per sprite

Eats a sprite away from the top down, with a colored line where it is being eaten. How far along it is and the color of the line are different for each sprite, so both travel with it.

`Shaders/Dissolve.fx`:

```hlsl
#include "Sprite.fxh"

// How tall the line is, 0..1 of the sprite. The same for every sprite: a plain parameter.
float EdgeWidth = 0.05;

float4 MainPS(SpritePixel input) : COLOR
{
    float4 color = SampleSprite(input);

    float amount = input.Custom0.x;      // Amount: 0 leaves the sprite whole, 1 removes all of it
    float4 edgeColor = input.Custom1;    // EdgeColor

    // LocalUV.y is 0 at the top of the sprite and 1 at its bottom, wherever it sits in its atlas page
    float gone = step(input.LocalUV.y, amount);
    float edge = (1 - gone) * step(input.LocalUV.y, amount + EdgeWidth) * step(0.001, amount);

    color.rgb = lerp(color.rgb, edgeColor.rgb * color.a, edge);
    return color * (1 - gone);
}

SPRITE_TECHNIQUE(MainPS)
```

The prototype:

```yaml
- type: shader
  id: Dissolve
  perSprite:
    Amount: custom0.x
    EdgeColor: custom1
  params:
    EdgeColor: [1, 0.5, 0, 1]     # what a sprite gets until it sets its own
```

And from code:

```csharp
_sprite.SetDrawShader(uid, "Dissolve");
_sprite.SetShaderParam(uid, null, "Amount", progress);
_sprite.SetShaderParam(uid, null, "EdgeColor", Color.Cyan);
```

Every dissolving sprite on screen, whatever its `Amount` and `EdgeColor`, is drawn in the same draw call.

---

## Using a shader

On a sprite, from a prototype (each layer can have its own `shader` too):

```yaml
components:
  - type: Sprite
    key: props/monitor
    shader: ScanLines
```

Over whatever shaders a sprite and its layers have, from code (nothing replicates):

```csharp
_sprite.SetDrawShader(uid, "Outline", Color.Yellow);   // the color goes to the DrawColor parameter
_sprite.SetDrawShader(uid, null);                       // back to its own
```

On a tilemap (see [Tilemaps](Tilemaps.md)):

```csharp
tilemap.Shader = "Grayscale";
```

When submitting to the renderer yourself:

```csharp
var shader = _shaders.GetSharedInstance("Grayscale");
_renderer.Submit(sprite, position, shader);

// with per sprite values
_renderer.Submit(sprite, position, shader, custom0: new Vector4(1, 0, 0, 1));
```

`ShaderManager.UnshadedInstance` is a built in instance with no `.fx` at all: it draws like no shader, but ignores the lighting. Use it for debug drawing.

---

## On the whole screen (post effects)

There is no separate kind of shader for the screen. A post effect draws the finished frame of a viewport as one sprite as big as the target, through any shader prototype:

- `SampleSprite(input)` is the pixel of the frame.
- `input.LocalUV` (and `input.UV`, they are the same here) runs 0..1 over the screen.
- `TexelSize` is one pixel of the frame, `ScreenSize` its size in pixels, `Time` works as always.

So `Grayscale` above already works as a post effect, unchanged:

```csharp
[Dependency] private readonly IRenderManager _renderer = default!;
[Dependency] private readonly ShaderManager _shaders = default!;

var gray = _shaders.GetUniqueInstance("Grayscale")!;
gray.SetParameter("Saturation", 0f);

_renderer.MainViewport.PostEffects.Add(gray);      // on
_renderer.MainViewport.PostEffects.Remove(gray);   // off
gray.Dispose();
```

`PostEffects` is a list on each `RenderViewport`, applied in order after everything else of that viewport; the engine UI is drawn after and is not affected. Each entry costs one full screen draw. Per sprite parameters get the prototype's starting values (there is only one "sprite"), so prefer plain parameters for a screen shader.

For anything more than "run the frame through a shader" (only part of the screen, a mask, several textures), write a [render overlay](Graphics.md#render-overlays) that asks for the screen texture.

---

## Lighting

A shader ignores the lighting when its prototype says `lit: false`. Nothing in the `.fx` decides it: the simplest unlit shader is a `.fx` that only returns `SampleSprite(input)`, with a prototype like this.

```yaml
- type: shader
  id: Unshaded
  lit: false
```

---

## Building

`Engine.ResourcesBuilder` compiles every `.fx` under the `Shaders/` folder of each resources folder when the game loads, and caches the result: a shader is only compiled again when it, or any header (`.fxh`), changed. The engine's `Sprite.fxh` lives in `Engine/Engine.Shared/EngineResources/Shaders/` and is made available to every shader, so `#include "Sprite.fxh"` just works. You can add headers of your own next to your `.fx` files and include them the same way.

To compile ahead of time instead (or from a tool):

```csharp
Engine.ResourcesBuilder.ShaderBuilder.Build();
```

It targets `DesktopGL` by default.

When the build itself breaks, loading logs `Building the shaders failed` and no shader at all is loaded. A shader that is missing afterwards is logged the first time something asks for it (`uses 'X.fx', which is not a compiled shader`). Common causes:

- A `technique` block written by hand, or no `SPRITE_TECHNIQUE` at the end.
- Declaring something the header already declares (`SpriteTexture`, `TexelSize`, `Time`...).
- A parameter listed under `params` that the `.fx` does not have: this one is a warning (`sets 'X', which 'Y.fx' does not have`), usually a typo or a per sprite parameter missing from `perSprite`.
