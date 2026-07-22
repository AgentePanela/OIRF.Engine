# Shaders

Custom shaders (`.fx` files) are compiled at runtime by `Engine.ResourcesBuilder`, a small project that wraps MonoGame's content pipeline. There is no `Content.mgcb`, no symlink setup, and no separate content project to maintain: you write the `.fx` file, wire up one method call, and the engine takes care of the rest.

---

## Setup

In `Program.cs` (or anywere before shader loading), you can call `ShaderBuilder.Build()` before starting the game.

```csharp
Engine.ResourcesBuilder.ShaderBuilder.Build();

using var game = new MyGame(options);
game.Run();
```

`ShaderBuilder.Build()` compiles every `.fx` file under `Resources/Shaders/` in your project, plus the engine's own shaders under `Engine/Engine.Shared/EngineResources/Shaders/`, targeting `DesktopGL`/`Reach` by default. The compiled shaders are picked up automatically at runtime by `ShaderManager`, keyed by file name (without extension). You can also pass the location of every resources folder it should compile shaders.

---

## Writing a Shader

Place your `.fx` file under `Resources/Shaders/`:

```
Resources/
  Shaders/
    Grayscale.fx
    MetallicFloor.fx
```

Assign it to a sprite from a prototype:

```yaml
components:
  - type: Sprite
    key: player/idle
    shader: Grayscale
```

Or to a tilemap (see [Tilemaps](Tilemaps.md)):

```csharp
tilemap.Shader = "MetallicFloor";
```

To fetch an `Effect` instance directly:

```csharp
[Dependency] private readonly ShaderManager _shaders = default!;

Effect? effect = _shaders.GetShader("Grayscale");
```

`ShaderManager.GetShader` returns the shared instance loaded for that name. If you're going to change its parameters per-draw, clone it first with `effect.Clone()` so you don't mutate the shared copy other draws are using.

---

## Automatic Lighting Support

Ordinary sprite shaders don't need to know anything about the lighting system: `ShaderLightingInjector` rewrites a copy of your `.fx` source in memory before compiling it (your file on disk is never touched), renaming your pixel shader entry point and wrapping it so its output gets multiplied by the scene's lightmap. This is the same idea as RobustToolbox's own shader wrapping: write a plain shader, get lighting for free.

A shader is left alone (not wrapped) if any of these are true:

- It declares `technique Unshaded`. Use this when you want the shader to always draw at full brightness, ignoring lighting entirely.
- It already declares `Texture2D LightMap`, meaning it was hand-written with lighting support already (like the engine's own `DefaultLit.fx`).
- It doesn't use `VertexShaderOutput` at all, meaning it's not a sprite shader (the internal lighting shaders such as `LightSoft.fx`, `ShadowDepth.fx`, `LightBlur.fx`, and `WallMerge.fx` fall in this category).

To opt a custom shader out of lighting, just declare an (unused) `technique Unshaded` block in it, following the same pattern as the engine's built-in `Unshaded.fx`.
