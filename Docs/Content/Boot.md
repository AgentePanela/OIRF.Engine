# Boot: from Program.cs to your first scene

This page walks through what happens between your `Program.cs` and the moment your game's `InitialScene` starts running, and shows how to write your own loading scene instead of using the engine's default one.

For a lower-level, contributor-facing view of the same sequence, see [Engine/Boot.md](../Engine/Boot.md).

---

## 1. Program.cs

Your `Program.cs` builds a `ClientOptions`, optionally builds shaders (see [Shaders](Shaders.md)), then constructs and runs your `GameClient` subclass:

```csharp
var options = new ClientOptions
{
    Title        = "My Game",
    Width        = 1280,
    Height       = 720,
    InitialScene = typeof(MyMainScene),
    Assemblies   = new[] { typeof(MyGame).Assembly },
};

Engine.ResourcesBuilder.ShaderBuilder.Build();

using var game = new MyGame(options);
game.Run();
```

`game.Run()` hands control over to MonoGame, which calls `Initialize()` and then starts ticking `Update()`/`Draw()` every frame.

---

## 2. GameState: Booting, Loading, Running

The engine tracks its own startup phase separately from MonoGame's lifecycle, through `GameClient.GameState`:

```csharp
public enum GameState
{
    Booting,
    Loading,
    Running,
}
```

- **Booting**: the first `Update()` call. IoC singletons are being registered, nothing is ready yet.
- **Loading**: a `LoadingScene` is active (see below), loading assets, prototypes, localization, and shaders, and preparing systems.
- **Running**: everything is ready. Your `InitialScene` is active and `EntityManager.Update()`/`Draw()` run every frame.

Don't spawn entities or touch prototypes before `GameState == Running`. If you need to react to that transition, subscribe to `LoadingFinishedEvent` (covered below) instead of polling `GameState`.

---

## 3. The Loading Scene

While `GameState == Loading`, `SceneManager` runs a special scene: an instance of `ClientOptions.LoadingScene` (a `Type`, defaulting to `typeof(DefaultLoadingScene)`). This type must derive from the abstract `LoadingScene` class; `SceneManager` validates that and throws if it doesn't.

`LoadingScene` drives its own state machine through four overridable methods:

```csharp
public abstract class LoadingScene : Scene
{
    protected virtual void StartLoading();
    protected virtual void TexturesPhase(float dt);
    protected virtual void RegistryPhase();
    protected virtual void LoadingCompleted();
}
```

| Method | Runs when | Default behavior |
|---|---|---|
| `StartLoading()` | Once, from `OnSceneStart()`, unless `_autoStartLoading` is set to `false` | Initializes the asset manager and calls `IFontManager.BootstrapDefaults()` |
| `TexturesPhase(dt)` | Every frame while textures are still loading | Ticks the asset manager's loading progress |
| `RegistryPhase()` | Once textures finish loading | Loads scene/component factories and initializes `EntityManager` on a background `Task` |
| `LoadingCompleted()` | Once, after the registry task finishes | Sets `GameState = Running` and raises `LoadingFinishedEvent` |

`LoadingScene` already has `_asset`, `_scene`, `_renderer`, `_entManager` available (inherited from `Scene`, see [Scenes](Scenes.md#accessing-engine-services-in-a-scene)), plus its own injected `_sceneFac`, `_compFac`, `_entMan`, `_fonts`, and `_textLayout`.

### LoadingFinishedEvent

Systems that need to run setup once loading is fully done (rather than in a scene's `OnSceneStart()`) can subscribe to this event instead:

```csharp
public sealed class MySystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeEvent<LoadingFinishedEvent>(OnLoadingFinished);
    }

    private void OnLoadingFinished(ref LoadingFinishedEvent ev)
    {
        // Safe to touch prototypes, atlas, etc. here.
    }
}
```

---

## 4. DefaultLoadingScene

`DefaultLoadingScene` is the engine's built-in `LoadingScene` implementation. It draws a centered "Loading..." label (driven by localization strings), keeps window resizing disabled while loading, and once `LoadingCompleted()` runs, either instantiates `ClientOptions.InitialScene` and switches to it, or (if `InitialScene` is left `null`) just disposes itself and stops.

For most games this default is enough. Write your own loading scene when you want a custom splash screen, a progress bar, or extra setup work tied to the loading phase.

---

## 5. Writing a Custom Loading Scene

Subclass `LoadingScene`, override whichever phases you need, and point `ClientOptions.LoadingScene` at it:

```csharp
public sealed class MyLoadingScene : LoadingScene
{
    private float _progress;

    public override void OnSceneStart()
    {
        base.OnSceneStart();
        // Set up your splash screen sprite/label here.
    }

    protected override void TexturesPhase(float dt)
    {
        base.TexturesPhase(dt); // keep ticking the asset manager
        _progress = 0.5f;       // update your own progress bar/UI here
    }

    public override void Draw(float dt)
    {
        base.Draw(dt);
        // Draw your splash screen / progress bar here.
    }

    protected override void LoadingCompleted()
    {
        base.LoadingCompleted(); // sets GameState = Running, raises LoadingFinishedEvent
        // If you skip base.LoadingCompleted(), the game never leaves the loading phase.

        var ops = GameClient.Options;
        if (ops.InitialScene is not null)
        {
            var scene = (Scene)Activator.CreateInstance(ops.InitialScene)!;
            _scene.ChangeScene(scene);
        }
    }
}
```

Wire it up in `Program.cs`:

```csharp
var options = new ClientOptions
{
    InitialScene = typeof(MyMainScene),
    LoadingScene = typeof(MyLoadingScene),
    // ...
};
```

That's it: `SceneManager` will instantiate `MyLoadingScene` instead of `DefaultLoadingScene` on startup and drive it the same way.
