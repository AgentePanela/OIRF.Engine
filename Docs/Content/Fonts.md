# Fonts

Text rendering is handled by `IFontManager` (accessible via `[Dependency]`, or `GameClient.InterfaceManager`'s controls resolve it themselves). Fonts are TrueType files (`.ttf`) rasterized on demand via FontStashSharp, so there is no content-pipeline step and no `.xnb` involved.

---

## File Structure

Place `.ttf` files inside a `Fonts/` folder under `Resources/` (same convention as [Textures](Resources.md#file-structure) and other resource folders). The engine scans both your game's `Resources/Fonts/` and the engine's own `EngineResources/Fonts/`:

```
Resources/
  Fonts/
    MyPixelFont.ttf
    MyPixelFont-Bold.ttf
```

Because rasterization happens on demand at whatever pixel size is requested, you don't need separate font assets per size.

---

## FontFamilyPrototype

Group up to 4 `.ttf` files (regular/bold/italic/boldItalic) under one family name via a `fontFamily` prototype:

```yaml
- type: fontFamily
  id: Arial
  regular: Arial           # file name, without extension
  bold: Arial-Bold
  italic: Arial-Italic
  boldItalic: Arial-BoldItalic
```

Only `regular` is required. Requesting a variant that isn't configured for a family falls back to that family's `regular`. The engine ships an `Arial` family (`Engine.Shared/EngineResources/Prototypes/fonts.yml`) as the default — the first `fontFamily` prototype loaded (by iteration order) is what `IFontManager` falls back to when no family is specified.

`FontVariant` is a `[Flags]` enum: `Regular`, `Bold`, `Italic` (combine `Bold | Italic` for bold-italic).

---

## IFontManager

```csharp
[Dependency] private readonly IFontManager _fonts = default!;

IReadOnlyCollection<string> families = _fonts.Families; // every fontFamily prototype ID

// Default family, at a given size
SpriteFontBase font = _fonts.Get(20f);

// A specific family/variant
SpriteFontBase bold = _fonts.Get(20f, "Arial", FontVariant.Bold);

// Font used when nothing else was specified
SpriteFontBase fallback = _fonts.GetFallback();

Vector2 size = _fonts.Measure("Hello, World!", 20f);
Vector2 sizeVariant = _fonts.Measure("Hello, World!", "Arial", 20f, FontVariant.Bold);
```

`Get` throws `InvalidOperationException` if the requested family doesn't exist as a `fontFamily` prototype *and* isn't a loose `.ttf` whose file name matches exactly (a loose file only ever stands in for `Regular` — it has no bold/italic pairing).

This is the same API the built-in UI controls (`Label`, `RichLabel`, text inputs — see [UI Controls](UIControls.md#text)) use for their `FontFamily`/`FontSize`/`FontVariant` style properties.

---

## Drawing World-Space Text (Label2D)

`Label2D` is a renderable (`IRenderable`) for drawing text through [`RenderManager`](Graphics.md), with optional shadow/outline:

```csharp
var label = new Label2D("Hello, World!", size: 20f, family: "Arial", FontVariant.Bold)
{
    Color = Color.White,
    ShadowEnabled = true,
    ShadowColor = Color.Black,
    ShadowOffset = new Vector2(1, 1),
};

_renderer.Submit(label, position);
```

Constructor overloads: `(string)` (default family/size), `(string, size)`, `(string, size, family, variant)`, or hand it a `SpriteFontBase` you already resolved yourself. `Label2D.WrapText(maxWidth)`/`TruncateText(maxWidth)` measure against its own `Font` and return a new string — they don't mutate `Label2D` itself. See [Label2D](Graphics.md#label2d) for the full renderable API (layering, rotation, scale).

> There is currently no named/semantic style layer (the old `FontKey`/`TextStyle` registries) on top of this — every `Label2D`/UI control picks its own family, size and color directly.
