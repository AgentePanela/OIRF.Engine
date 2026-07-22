# ORIF Engine Documentation

Welcome to the **ORIF Engine** documentation.

ORIF is a custom 2D game engine built on top of [MonoGame](https://monogame.net/). It provides a complete ECS (Entity-Component-System) architecture, prototype-driven content authoring via YAML, scene management, a layered renderer, physics, tilemaps, localization, and more.

---

## Documentation Sections

### 📦 Content — For Game Developers
Everything you need to build a game on top of ORIF.

| Page | Description |
|------|-------------|
| [Home](Content/Home.md) | Engine API overview |
| [Getting Started](Content/GettingStarted.md) | Project setup, entry point, and game boot |
| [Boot](Content/Boot.md) | Startup sequence from Program.cs to your first scene, and custom loading scenes |
| [ECS](Content/Ecs.md) | Entities, Components, Systems, and Events |
| [Scenes](Content/Scenes.md) | Scene lifecycle and scene management |
| [Prototypes](Content/Prototypes.md) | YAML-driven data definitions |
| [Graphics](Content/Graphics.md) | Rendering, sprites, labels, shapes, and camera |
| [Animations](Content/Animations.md) | Spritesheet/frame-based animations via info.yml |
| [Lighting](Content/Lighting.md) | Dynamic 2D lighting, shadows, and ambient light |
| [Fonts](Content/Fonts.md) | Font registration, FontKey, and TextStyle |
| [Shaders](Content/Shaders.md) | Custom shaders and the resource builder pipeline |
| [Input](Content/Input.md) | Keyboard, mouse, gamepad, and action maps |
| [Physics](Content/Physics.md) | Collision, physics components, and raycasting |
| [Tilemaps](Content/Tilemaps.md) | Tile-based maps and chunks |
| [Resources](Content/Resources.md) | Asset loading and the texture atlas |
| [Localization](Content/Localization.md) | Multi-language support with Fluent |
| [Tags](Content/Tags.md) | Entity tagging system |
| [Storage](Content/Storage.md) | Persistent user data (saves, config) |
| [UI](Content/UI.md) | UI canvases, windows, and widgets |

### 🔧 Engine — For Engine Developers
Internals and extension guides for contributors.

| Page | Description |
|------|-------------|
| [Engine Home](Engine/Home.md) | Engine architecture overview *(Under Development)* |

### 🚲 Migration - Updating from one version to another.
| Page | Description |
|------|-------------|
| [PR0.2.0 > PR0.3.0](Migrating/PR2-to-PR3.md) | Learn how to migrate from pre-release 0.2.0 > pr0.3.0 |

---

## Assembly Overview

| Assembly | Purpose |
|----------|---------|
| `Engine.Shared` | ECS core, prototypes, physics, IoC, locale, storage |
| `Engine.Client` | Rendering, input, audio, scenes, tilemaps, UI |
| `Engine.Server` | Server-side game logic |
