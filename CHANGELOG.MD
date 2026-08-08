# Pre-Release
Versions released before the engine first full release

## PR 0.5.0?

## PR 0.4.0
08/08/2026

### New Features
- Audio system rewrite: `SharedAudioSystem`, `AudioComponent`, `AudioListenerComponent` and `SharedAudioManifest` replace the old audio code, adding spatial (3D) audio with proper distance attenuation/panning via MonoGame's `AudioListener`/`AudioEmitter`, fading, sound tags, and server-side audio support. See `Docs/Content/Audio.md`.
- Basic threading support: new `Engine.Shared.Threading` namespace (`IParallelManager`, `IJob`, `IParallelJob`, `IParallelRangeJob`) dispatches work onto the thread pool, falling back to inline execution for workloads under a job's minimum batch size. `MainThread.AssertMainThread()` (Debug-only) catches ECS state touched off the main thread, and `MainThread.SafeThreads` lets a background thread opt in for bounded stretches of work (e.g. boot). Tunable via the `threading.parallelprocesscount` CVar.
- Transform parenting: `TransformComponent.Parent` plus a new `TransformSystem` that moves/rotates children along with their parent every tick and cascades entity deletion to children.
- Animation layers: `AnimationComponent.Layers` lets individual `SpriteComponent` layers play their own animation independently from the base sprite, via `AnimationSystem.SetLayerAnimation`/`PauseLayer`/`ResumeLayer`. Animation events now carry a `LayerId`.
- Touch input: `InputManager` reads touch via MonoGame's `TouchPanel` (`AnyTouchTapped`/`AnyTouchDown`/`AnyTouchReleased`, `TouchScreenPosition`/`TouchWorldPosition`) and emulates the left mouse button from touch by default (`input.EmulateMouseFromTouch`).
- File system abstraction: `IFileSystem`/`FileSystem`/`DesktopFileSystem` decouple resource and user-data I/O from the disk, wired once through `SharedResourceManager`.
- Hot reloading for prototypes, locale `.ftl` files, and audio banks, on top of the shader hot reloading already in 0.3.0.
- `SpriteLayer.Offset` positions a layer relative to the main sprite (rotated with the entity)
- Abstract `EntitySystem` base classes can now be injected/resolved like concrete ones (`EntityManager.Systems`).
- `EntityManager.WipeAllEntities()` clears the entire entity set at once.
- Compiled shader cache is now encrypted at rest (`ShaderCrypto`, AES-GCM)
- `ClientOptions.SelfContainedDataPath` stores user data next to the executable instead of `%AppData%`.

### Improvements
- Lighting: further occluder offset/seam fixes on top of the 0.3.0 rework, plus a lighting tab in the debug window and an occluder debug draw system.
- Shaders now compile during game loading with progress reported through the loading scene, and the resource builder gained finer build control.
- `RenderManager` sprite depth-sorting refactored.
- Fixed dictionary (de)serialization and raw-section output in `DataFieldConverter`, and entity prototype id no longer failing to propagate.
- New `[AnimationKeyAttribute]`, `[ShaderKeyAttribute]`, `[TextureKeyAttribute]` resource key attributes for editor/tooling integration.
- `EntityUid` gained an `Invalid` check.
- `.sln` replaced by `.slnx` (new XML-based solution format).

### Bugfixes
- Fixed render target viewport rendering and a `Camera2D` viewport bug.
- Fixed `TilePrototype.Sprite` not actually being required.
- Fixed engine version string.

### Breaking Changes
- `OIRF.Engine.sln` was removed in favor of `OIRF.Engine.slnx`.
- The old audio API surface was replaced by `SharedAudioSystem`/`AudioComponent`/`AudioListenerComponent` - see `Docs/Content/Audio.md`.

## PR 0.3.0

### New Features
- Vector shape rendering: circles, rectangles, ellipses, lines, hexagons, triangles, arcs, and rings, drawn directly through `RenderManager` (`DrawCircle`, `DrawRect`, `DrawLine`, etc.), backed by Apos.Shapes.
- New shader/resource build pipeline (`Engine.ResourcesBuilder`). Shaders are compiled at Debug startup with a single `ShaderBuilder.Build()` call, replacing the old Contentless + symlink + `Content.mgcb` setup.
- Automatic lighting injection for sprite shaders: a plain `.fx` shader gets lighting support wrapped around it for free, unless it opts out with `technique Unshaded`.
- Shader support for tilemaps via `TilemapComponent.Shader`.
- Entity cloning: `EntityManager.CloneEntity(uid)` copies an entity's live component state onto a new entity.
- Rewritten loading scene lifecycle (`LoadingScene`, `DefaultLoadingScene`, `ClientOptions.LoadingScene`, `LoadingFinishedEvent`), so games can plug in a custom splash screen/progress bar instead of the default loading screen.
- Z-order (`Depth`) sorting within a render `Layer`, plus `SpriteSystem.BringToFront`/`SendToBack` helpers.
- Simulation pause support for systems.
- Ability to capture the rendered viewport as a texture.

### Improvements
- Lighting system rework: occluder edge geometry is now built once per frame instead of per light, wall bleed and light blur passes were refined, and unshaded sprites now respect `Layer`/`Depth` against the rest of the scene instead of always drawing on top.
- Shape rendering pass improved and given proper unshaded support.
- Engine.Server improvements.

### Bugfixes
- Fixed a `NullReferenceException` caused by the lighting system.
- Fixed font loading issues introduced by the new content builder.
- Various editor stability fixes.

### Breaking Changes
- The old content pipeline (Contentless nuget, `Content.mgcb`, `EngineShaders` symlink) is gone. See the [migration guide](Engine/Docs/Migrating/PR2-to-PR3.md) for the new setup.
- `IFontManager.Load(ContentManager, ...)` and `TryLoadFirstAvailable(...)` were removed. Fonts are now TrueType files loaded straight from disk (`DefaultFontCatalog` was replaced by `DefaultFontSizes`).
- `GraphicsProfile` is now forced to `HiDef` (required by the new shape rendering library).

## PR 0.2.0
- Lightinings system
- Improved Engine.Server
- Engine own resources folder
- Animation support
- EntityDrawSystem for systems that need to use the Draw() calls (only for Client)
- Draw() from EntitySystem has been removed
- EntityPrototype API has been updated to have easy sprite access in client-side

## PR 0.1.0
- First engine dev release.