# Engine Developer Documentation

Welcome to the **ORIF Engine** contributor documentation.

This section covers the engine's internal implementation details, registration sequences, graphics pipeline details, and architecture design guidelines.

> [!NOTE]
> This section is currently under development as part of Phase 2 of the documentation project.

## Planned Topics

- **Booting sequence & IoCManager internals**
- **Adding new engine systems**
- **Graphics pipeline & custom rendering layers**
- **Serialization & custom DataField converters**
- **Prototype loading lifecycle**
- **Physics system extension**

> [!NOTE]
> All content below and linked are under development content and may be bad and dated

## Architecture Chapters

Explore the internal engine subsystems:

* [Startup & Lifecycle (Boot)](Boot.md) — Understanding the startup sequence, assembly scanning, and initialization phases.
* [Serialization Internals](Serialization.md) — How prototypes are parsed from YAML, reflection mapping, and prototype inheritance merges.
* [Physics & Collision Loop](Physics.md) — Overlap detection algorithms, collision layers and masks checking, and contact resolution math.
* [Tilemap & Auto-tiling Engine](Tilemaps.md) — Chunked mesh optimization, viewport culling, and auto-tiling edge calculation.
* [Rendering Pipeline](Graphics.md) — RenderManager draw submit queue, viewport adapters, and custom shader compilation.

---

## Codebase Structure

The codebase is split into three main engine components and content layers:

```
c:\Users\<user>\Documents\Project ORIF\
├── Engine.Shared/             # Core shared logic (ECS base, Serialization, Physics, Tags)
├── Engine.Client/             # Client-specific code (MonoGame GameClient, Rendering, UI, Audio)
├── Engine.Server/             # Server-specific skeleton code
├── Content.Client/            # Client game content (Player, movement systems, menus)
├── Content.Server/            # Server game content
├── Resources/                 # Asset configurations (YAML prototypes, FTL locales, textures)
└── Project ORIF.sln           # Main Visual Studio / MSBuild solution file
```

### Component Roles

1. **Engine.Shared**: Must not contain any client-side graphics dependencies (no MonoGame `ContentManager` or UI canvas instances). It handles standard database prototypes, files storage, event-bus routing, and baseline physics.
2. **Engine.Client**: Integrates MonoGame rendering ticks, audio channels, Myra UI event hooks, and viewport adapters. It depends on `Engine.Shared`.
3. **Engine.Server**: Operates as a headless process running the simulation tick loop without rendering viewports.