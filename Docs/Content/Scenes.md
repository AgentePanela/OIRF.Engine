# Scenes

Scenes are the primary way to structure your game. Each scene is an isolated world with its own entities and components. Only one scene can be active at a time.

---

## Creating a Scene

Inherit from `Scene` and override the lifecycle methods you need:

```csharp
using Engine.Client.Scenes;
using Engine.Client.UI;

public sealed class GameplayScene : Scene
{
    // Optionally set a default UI canvas for this scene (null = no UI)
    public override UICanvas? DefaultCanvas { get; protected set; } = null;

    public override void OnSceneStart()
    {
        // Called after the scene is initialized and all entities are ready.
        // Spawn your starting entities here.
        var uid = CreateEmptyEntity("Player");
        EnsureComp<TransformComponent>(uid).Position = new Vector2(200, 300);
    }

    public override void Update(float dt)
    {
        // Per-frame update logic for the scene itself.
        // Entity systems handle most game logic — use this for scene-level state.
    }

    public override void Draw(float dt)
    {
        // Scene-level draw calls (rarely needed; prefer entity systems).
    }

    public override void OnSceneEnd()
    {
        // Called before the scene is disposed.
        // All entities are still alive here.
    }
}
```

---

## Scene Lifecycle

```
SceneManager.ChangeScene(next)
        │
        ▼
  [Dispose current scene]
        │
        ├─ OnSceneEnd()        ← scene override
        ├─ Components cleared
        └─ Entities cleared
        │
        ▼
  [Initialize next scene]
        │
        ├─ ForceScene → EntityManager switches context
        ├─ Bootstrap loaded entities (fires CompAddedEvent + EntityAddedEvent)
        └─ OnSceneStart()      ← scene override
```

### Lifecycle Method Reference

| Method | When it is called |
|--------|-------------------|
| `OnSceneStart()` | After initialization; entities from the scene file are already loaded |
| `Update(float dt)` | Every frame while this scene is active |
| `Draw(float dt)` | Every frame during the draw pass |
| `OnSceneEnd()` | Before disposal; entities are still alive |

---

## SceneManager

`SceneManager` (accessible via `GameClient.Scenes`) handles transitions between scenes.

### Switching Scenes

```csharp
// Switch to a new scene instance on the next frame
GameClient.Scenes.ChangeScene(new GameplayScene());
```

The transition happens at the **beginning of the next Update call**, before the new scene's update runs. The current scene's `OnSceneEnd()` is called and it is disposed before the new scene initialises.

> **Important:** `ChangeScene` is safe to call from any context, including inside `Update` or event handlers. The actual transition is deferred.

### Events

```csharp
// Fires after the new scene has fully initialized
GameClient.Scenes.OnSceneChanged += scene => { ... };

// Fires right before Initialize() is called on the new scene
GameClient.Scenes.OnBeforeSceneInit += scene => { ... };
```

### Current Scene

```csharp
Scene? current = GameClient.Scenes.CurrentScene;
```

---

## DefaultCanvas

Each scene can declare a default `UICanvas`. When the scene loads, the engine automatically installs the canvas as the active UI screen.

```csharp
public override UICanvas? DefaultCanvas { get; protected set; } = new MyHUD();
```

Set to `null` to use no UI (or manage UI manually via `UIManager`).

See the [UI docs](UI.md) for details on creating canvases.

---

## Accessing Engine Services in a Scene

`Scene` has several pre-injected dependencies for convenience:

```csharp
protected GameClient _game;        // GameClient instance
protected SceneManager _scene;     // SceneManager
protected IAssetManager _asset;    // Asset manager
protected RenderManager _renderer; // Render manager
protected EntityManager _entManager; // Entity manager
```

These are filled automatically during initialization via IoC — no manual injection required.
