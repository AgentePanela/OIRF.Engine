## Migrating from pre-release 0.2.0 to pre-release 0.3.0
The whole content/shader setup from 0.2.0 is gone. If your project still has the `Contentless` package, a `Content.mgcb` file, or an `EngineShaders` symlink from following the old steps below, remove all of that: it's replaced by a small in-process shader builder.

### 1. Remove the old Contentless setup

Delete, if present:
- The `Contentless` package reference and the `<MonoGameContentReference Include="Content\Content.mgcb" />` item in your game csproj.
- Your `Content\Content.mgcb` file and `Content\Contentless.json`.
- The `EngineShaders` symlink inside your Content folder.

### 2. Reference Engine.ResourcesBuilder

Add a project reference to the new resource builder project in your game csproj:

```xml
<ItemGroup>
    <ProjectReference Include="..\Engine\Engine.ResourcesBuilder\ResourcesBuilder.csproj" />
</ItemGroup>
```

### 3. Build shaders at startup

In `Program.cs`, call `ShaderBuilder.Build()` before `game.Run()`

```csharp
Engine.ResourcesBuilder.ShaderBuilder.Build();

using var game = new MyGame(options);
game.Run();
```

### 4. Move your shaders

Custom `.fx` shaders now live in a plain `Resources\Shaders\` folder (no symlink, no `Content.mgcb`, no manual `.xnb` step). `ShaderBuilder.Build()` compiles them automatically and `ShaderManager` picks them up at runtime by file name.

See [Shaders](../Content/Shaders.md) for the full picture, including how lighting support gets injected into ordinary sprite shaders automatically.

### 5. Publishing

`ShaderBuilder.Build()` is meant to run in Debug only; Release publishes skip it on purpose. Run a Debug build at least once before publishing so the compiled `.xnb` files exist to be copied into the publish output. See the `ContentPipelinePublish` MSBuild target in `Project.Eptus.csproj` for a working example of copying `Resources\` and the built `Content\**\*.xnb` into a publish folder.

---

## Other relevant changes

- `EntryPointOptions` was renamed to `ClientOptions` (same fields, plus `LoadingScene` and a now-nullable `InitialScene`, see [Getting Started](../Content/GettingStarted.md)).
- `IFontManager.Load(ContentManager, ...)` and `TryLoadFirstAvailable(...)` were removed. Fonts are now TrueType files loaded directly from disk, not compiled through the content pipeline. See [Fonts](../Content/Fonts.md) if your game called these directly.
- `GraphicsProfile` is now forced to `HiDef` (required by the shape rendering library, `Apos.Shapes`). If your game explicitly requested `Reach`, that setting no longer applies.