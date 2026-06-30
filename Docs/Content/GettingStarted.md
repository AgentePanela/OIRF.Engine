# Getting Started

This page shows how to create a new game project using the ORIF Engine, configure the entry point, and understand the boot lifecycle.

---

## 1. Project Setup

Create a new C# project and reference `Engine.Client`. Your game code lives in a separate assembly (e.g. `Content.Client`) that you pass to the engine at startup.

---

## 2. Entry Point

Subclass `GameClient` and call it from your `Program.cs`:

```csharp
using Engine.Client;

public sealed class MyGame : GameClient
{
    public MyGame(EntryPointOptions options) : base(options) { }
}

class Program
{
    static void Main(string[] args)
    {
        var options = new EntryPointOptions
        {
            Title        = "My Awesome Game",
            Version      = "1.0.0",
            Width        = 1280,
            Height       = 720,
            InitialScene = typeof(MyMainScene),
            DataPath     = Path.Combine("MyStudio", "MyGame"),
            Assemblies   = new[] { typeof(MyGame).Assembly }
        };

        using var game = new MyGame(options);
        game.Run();
    }
}
```

`GameClient` is a singleton — only one instance may exist at a time.

---

## 3. EntryPointOptions

`EntryPointOptions` controls the engine's startup behaviour. All fields have sensible defaults.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Title` | `string` | `"My Game"` | Window title |
| `Version` | `string` | `"1.0.0"` | Game version (stored in CVars) |
| `Width` | `int` | `1280` | Preferred back-buffer width |
| `Height` | `int` | `720` | Preferred back-buffer height |
| `FullScreen` | `bool` | `false` | Start in fullscreen |
| `BackgroundColor` | `Color` | `Color.Black` | Clear colour each frame |
| `WindowResizing` | `bool` | `true` | Allow window resize |
| `InitialScene` | `Type` | — | **Required.** The first scene type to load |
| `DataPath` | `string` | `"MyCompany\MyGame"` | Subfolder inside `%AppData%` for user data |
| `SaveConfigOnExit` | `bool` | `false` | Auto-save CVars on shutdown |
| `PauseOnUnfocus` | `bool` | `false` | Freeze update loop when window loses focus |
| `TextureAtlasSize` | `AtlasSize` | `Size2048` | Texture atlas page dimensions |
| `Assemblies` | `Assembly[]` | `[]` | Content assemblies for prototype & component scanning |
| `Samplimg` | `SamplerState` | `PointClamp` | Default sampler for sprites |
| `Args` | `string[]` | `[]` | Raw command-line arguments |

> **Tip:** Always include your game's assembly in `Assemblies` so that the engine can discover your custom components, systems, and prototypes.

---

## 4. GameClient Static Accessors

Once the game is running you can access engine subsystems from anywhere via `GameClient`:

```csharp
GameClient.EntityManager   // EntityManager
GameClient.Scenes          // SceneManager
GameClient.Renderer        // RenderManager
GameClient.InputManager    // InputManager
GameClient.Assets          // IAssetManager
GameClient.Audio           // IAudioManager
GameClient.Prototypes      // IPrototypeManager
GameClient.FontManager     // IFontManager
GameClient.Camera          // Camera2D
GameClient.Viewport        // ViewportAdapter
GameClient.InterfaceManager // UIManager
GameClient.WindowManager   // WindowManager
GameClient.ConfigManager   // IConfigurationManager
GameClient.GameTime        // GTime  (delta time, FPS, etc.)
GameClient.GameState       // Current GameState enum value
```

---

## 5. Game State Lifecycle

The engine cycles through the following states automatically:

```
Booting  →  Loading  →  Running
```

| State | Description |
|-------|-------------|
| `Booting` | First frame. Window is not yet rendered. |
| `Loading` | The loading scene is active. Engine systems and resources are being initialized. |
| `Running` | All systems are ready. Your game code runs every frame. |

`EntityManager.Update()` and the ECS are **only called** while the state is `Running`. Wait for `Running` before spawning entities or running game logic that depends on loaded prototypes.

---

## 6. Next Steps

- Learn about the [ECS architecture](Ecs.md) to start building gameplay.
- Define your first game objects with [Prototypes](Prototypes.md).
- Organize your game into [Scenes](Scenes.md).
