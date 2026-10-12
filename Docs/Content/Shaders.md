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

## Lighting

A shader whose technique is named `Unshaded` is flagged as unshaded by `RenderManager` (see `Unshaded.fx`), which is how a shader opts out of lighting.
