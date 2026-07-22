# Fonts

Text rendering is handled by `IFontManager` (accessible via `GameClient.FontManager`). Fonts are TrueType files (`.ttf`) rasterized on demand, so there is no content-pipeline step and no `.xnb` involved.

---

## File Structure

Place `.ttf` files inside a `Fonts/` folder under `Resources/` (same convention as [Textures](Resources.md#file-structure) and other resource folders). The engine scans both your game's `Resources/Fonts/` and the engine's own `EngineResources/Fonts/`:

```
Resources/
  Fonts/
    MyPixelFont.ttf
```

Every `.ttf` found is added to a single shared `FontManager.MyraFontSystem` ([FontStashSharp](https://github.com/FontStashSharp/FontStashSharp)) at startup. Because rasterization happens on demand at whatever pixel size is requested, you don't need separate font assets per size.

---

## FontKey

`FontKey` is a small enum of the engine's built-in font roles:

```csharp
public enum FontKey
{
    None = 0,
    Default,
    UiBody,
    UiTitle,
    Debug,
    Loading,
    Tooltip,
    Button,
    UiSmall,
    Notification,
}
```

Each key resolves to a `SpriteFontBase` registered via `IFontManager.Register`. `IFontManager.BootstrapDefaults()` registers all of the built-in keys automatically (called during the loading scene, see [Boot](Boot.md)); you don't need to call it yourself, but it's safe to call again.

### Default Sizes

Each `FontKey` has a default rasterization size, held in the static `DefaultFontSizes` registry:

```csharp
float size = DefaultFontSizes.Get(FontKey.UiBody); // 16f by default

DefaultFontSizes.Set(FontKey.UiBody, 20f); // override before BootstrapDefaults() runs
```

| FontKey | Default Size |
|---|---|
| `Default` | 16 |
| `UiBody` | 16 |
| `UiTitle` | 24 |
| `Debug` | 13 |
| `Loading` | 16 |
| `Tooltip` | 14 |
| `Button` | 16 |
| `UiSmall` | 12 |
| `Notification` | 15 |

---

## IFontManager

```csharp
[Dependency] private readonly IFontManager _fonts = default!;

SpriteFontBase font = _fonts.Get(FontKey.UiBody);
bool exists          = _fonts.Has(FontKey.UiBody);
bool found           = _fonts.TryGet(FontKey.UiBody, out var maybeFont);
SpriteFontBase fallback = _fonts.GetFallback(); // FontKey.Default, or the first registered font

Vector2 size = _fonts.Measure(FontKey.UiBody, "Hello, World!");
```

Registering a custom font under one of the built-in keys (or your own convention) is a matter of loading it from `FontManager.MyraFontSystem` and registering it:

```csharp
var font = FontManager.MyraFontSystem.GetFont(18f);
_fonts.Register(FontKey.UiBody, font);
```

---

## TextStyle

For most UI/game text you don't need to touch `FontKey` directly. Use a `TextStyle` instead, which bundles a `FontKey`, size, color, and shadow/outline effects into one reusable definition:

```csharp
public enum TextStyle
{
    None = 0,
    Body,
    Title,
    Debug,
    Loading,
    Tooltip,
    Button,
    ButtonText,
    Caption,
    Notification,
}
```

`TextStyleLibrary` (injected, or resolved through `IFontManager.GetForStyle`) maps each `TextStyle` to a `TextStyleDefinition`:

```csharp
public sealed class TextStyleDefinition
{
    public FontKey FontKey { get; set; }
    public Color Color { get; set; }
    public float Scale { get; set; }
    public float Size { get; set; }
    public bool ShadowEnabled { get; set; }
    public Color ShadowColor { get; set; }
    public Vector2 ShadowOffset { get; set; }
    public bool OutlineEnabled { get; set; }
    public Color OutlineColor { get; set; }
    public int OutlineThickness { get; set; }
}
```

```csharp
SpriteFontBase font = _fonts.GetForStyle(TextStyle.Title);
Vector2 size         = _fonts.Measure(TextStyle.Title, "Game Over");
```

To override a style (e.g. reskin `Title` for your game), resolve `TextStyleLibrary` and call `Set`:

```csharp
[Dependency] private readonly TextStyleLibrary _styles = default!;

_styles.Set(TextStyle.Title, new TextStyleDefinition(FontKey.UiTitle, Color.Gold)
{
    Size = 32f,
    ShadowEnabled = true,
});
```

---

## Drawing Text

See [Label2D](Graphics.md#label2d) for how to submit text through the render queue using a `FontKey` or `TextStyle`.
