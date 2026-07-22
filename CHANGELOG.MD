# Pre-Release
Versions released before the engine first full release

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