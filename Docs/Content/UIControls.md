# UI Controls Reference

Every built-in control under `Engine.Client.UI`. See [UI](Ui.md) first for the `Control` base (layout, styling, input) that all of these build on — this page only covers what each concrete control adds.

All snippets assume `using Engine.Client.UI;`.

---

## Containers

Containers arrange their children; they don't draw content of their own beyond `PanelContainer`'s optional background/outline/texture.

| Control | Layout |
|---|---|
| `PanelContainer` | Stacks every child in the same rect; each one aligns itself independently within it. Base class for most other containers — see [Background & Borders](#background--borders-panelcontainer) below. |
| `BoxContainer` | Lays children out one after another along `Orientation` (`Horizontal`/`Vertical`), with a `Separation` gap. |
| `GridContainer` | Uniform grid with a fixed `Columns` count, wraps to a new row automatically; `HSeparation`/`VSeparation` gaps. |
| `CenterContainer` | Centers a single child within itself, at the child's own desired size. |
| `LayoutContainer` | Free positioning via anchors (fractions of its own rect) — see [Anchored Positioning](#anchored-positioning-layoutcontainer). |
| `ScrollContainer` | One child, clipped, with scrollbars — see [ScrollContainer](#scrollcontainer). |
| `SplitContainer` | Exactly two children with a draggable divider between them. |
| `TabContainer` | A tab bar plus one visible page at a time. |

### Background & Borders (`PanelContainer`)

```csharp
var panel = new PanelContainer
{
    Background = new Color(30, 30, 35),      // ColorGradient — implicit from Color, or a real gradient
    OutlineColor = Color.Gray,
    OutlineThickness = new Thickness(1),
    OutlineMargin = new Thickness(0),        // negative pushes the outline outward past Bounds
    TextureKey = "panel_9slice",             // draws a 9-slice/sprite instead of Background
    NineSliceMargins = new Thickness(4),
    Tint = Color.White,
};
```

`ColorGradient` has factories for common cases — `ColorGradient.Horizontal(left, right)`, `.Vertical(top, bottom)`, `.Diagonal(topLeft, bottomRight)`, `.Radial(center, edge)` — or construct one directly for full control over shape/repeat/offsets. In YAML it's `{ colorA, colorB, pointA, pointB, shape, repeat, offsetA, offsetB }` (`pointA`/`pointB` are fractions of the control's own rect), or just a plain color string.

### BoxContainer

```csharp
var row = new BoxContainer { Orientation = Orientation.Horizontal };
row._separation = 8; // or set via stylesheet: separation: 8

row.AddChild(new Label { Text = "Name:" });
row.AddChild(new LineEdit { HorizontalExpand = true }); // claims leftover space
```

### GridContainer

```csharp
var grid = new GridContainer(); // columns: 3 via stylesheet, or set the backing field
for (var i = 0; i < 9; i++)
    grid.AddChild(new Button($"{i}"));
```

### Anchored Positioning (`LayoutContainer`)

For free/overlapping placement instead of stacking — think Godot's `Control` anchors:

```csharp
var root = new LayoutContainer();
var hpBar = new ProgressBar();
root.AddChild(hpBar);

LayoutContainer.SetAnchorPreset(hpBar, LayoutPreset.TopLeft);
LayoutContainer.SetPosition(hpBar, new Vector2(16, 16));
LayoutContainer.SetSize(hpBar, new Vector2(200, 20));
```

`LayoutPreset`: `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`, `Center`, `CenterTop`, `CenterBottom`, `Wide` (stretch both axes). `WindowManager` uses `LayoutPreset.Center` to place newly opened windows.

### ScrollContainer

```csharp
var scroll = new ScrollContainer { ScrollSpeed = 50f };
scroll.AddChild(longContent); // redirected to its internal viewport

Vector2 offset = scroll.ScrollOffset;
Vector2 max = scroll.MaxScrollOffset;
```

Shows/hides its own `ScrollBar`s (also usable standalone: `Value`, `MaxValue`, `Page`, `OnValueChanged`) as needed. `ItemList` is built on top of `ScrollContainer`.

### SplitContainer

```csharp
var split = new SplitContainer { Orientation = Orientation.Horizontal };
split.AddChild(leftPane);   // first child
split.AddChild(rightPane);  // second child
split.SplitOffset = 300f;   // divider position in pixels from the start; -1 (default) = 50/50 on first arrange
```

### TabContainer

```csharp
var tabs = new TabContainer();
tabs.AddTab("Stats", statsPage);
tabs.AddTab("Inventory", inventoryPage);
tabs.RemoveTab(0);
tabs.CurrentTab = 1;
tabs.OnTabChanged += index => { };
```

`AddChild`/`RemoveChild` are blocked on `TabContainer` and `ItemList` — use `AddTab`/`AddItem` instead, same as `Window` redirects `AddChild` to `Contents`.

---

## Buttons

| Control | Adds over `BaseButton` |
|---|---|
| `BaseButton` | Abstract. Press/hover/disabled state, `ToggleMode`, a single `Content` child, `OnClick`/`OnToggled` events. |
| `Button` | A centered text label (`Text`). |
| `CheckBox` | A checkbox icon plus optional label. |
| `CheckButton` | A switch-style toggle plus optional label. `ToggleMode` is on by default. |
| `OptionButton` | Opens a popup list to pick one of several text options. |

```csharp
var btn = new Button("Play") { MinWidth = 120 };
btn.OnClick += mouseButton => StartGame();

var toggle = new CheckBox { Text = "Fullscreen" };
toggle.ToggleMode = true;
toggle.OnToggled += isOn => SetFullscreen(isOn);

var mode = new OptionButton();
mode.AddItem("Easy");
mode.AddItem("Normal");
mode.AddItem("Hard");
mode.OnItemSelected += index => { };
mode.SelectedIndex = 1;
```

`BaseButton` also exposes `Disabled` (ignores input, adds the `disabled` pseudo-class) and `Pressed` (readable/settable directly, e.g. for a toggle you drive from game state instead of clicks).

Any control assigned to `BaseButton.Content` — and its whole subtree — automatically mirrors the button's `hover`/`pressed`/`focus`/`disabled` pseudo-classes if it shares a `StyleAlias` with the button (built-in buttons already tag their internal `Label` this way), so a single stylesheet rule like `{ control: button, pseudoClass: disabled }` reaches the label's color too.

---

## Text

| Control | Purpose |
|---|---|
| `Label` | Plain single/multi-line text. |
| `RichLabel` | Text with inline `[tag]` BBCode-style markup. |
| `LineEdit` | Single-line text input. |
| `TextEdit` | Multi-line text input with its own scrollbar. |

### Label

```csharp
var label = new Label
{
    Text = "Score: 0",
    FontSize = 20,
    Color = Color.White,
    TextAlign = HorizontalAlignment.Center,
    TextVerticalAlign = VerticalAlignment.Center,
    TextTransform = TextTransform.Uppercase,
    AutoWrap = true, // wraps instead of overflowing available width
};
```

`FontFamily` (a `fontFamily` prototype ID, e.g. `"Arial"` — see `Resources/Prototypes/fonts.yml`) and `FontVariant` (`[Flags]`: `Regular`, `Bold`, `Italic`, combinable) pick which face to rasterize; leave `FontFamily` null to use the font manager's default family. Same `IFontManager` these controls resolve internally is also what world-space text (`Label2D`) draws with — see [Fonts](Fonts.md).

### RichLabel

```csharp
var rich = new RichLabel
{
    Text = "[b]Bold[/b], [i]italic[/i], [color=#FF0000]red[/color], [size=24]big[/size]",
};
```

Built-in tags: `b` (bold), `i` (italic), `u` (underline), `s` (strikethrough), `color=<hex>`, `font=<family>`, `size=<px>`. Register a custom one:

```csharp
public sealed class ShakeTag : IMarkupTag
{
    public string Name => "shake";
    public FormattedStyle Apply(FormattedStyle current, string? value) => current; // style-only hook today
}

MarkupTagRegistry.Register(new ShakeTag());
```

### LineEdit / TextEdit

```csharp
var input = new LineEdit
{
    PlaceholderText = "Enter name...",
    MaxLength = 20,
};
input.OnTextChanged += text => { };
input.OnTextEntered += text => Submit(text); // Enter pressed

var multiline = new TextEdit { Rows = 4 };
```

Both derive from `BaseTextInput`, which already implements caret movement, click/drag/double/triple-click selection, clipboard copy/cut/paste, and undo/redo — `Text`, `ReadOnly`, `MaxLength`, `PlaceholderText`, `OnTextChanged` are all inherited from there. Neither needs manual keyboard wiring: focusing one shows the virtual keyboard on platforms that need it (`WantsVirtualKeyboard`).

---

## Lists & Data Views

### ItemList

```csharp
var list = new ItemList { SelectMode = ItemList.ListSelectMode.Single };
list.AddItem("Sword");
list.AddItem("Shield", iconKey: "icon_shield");
list.OnSelectionChanged += index => { };
int selected = list.SelectedIndex;          // Single mode
IReadOnlyList<int> many = list.SelectedIndices; // Multiple mode
list.Select(0);
list.RemoveItem(1);
list.Clear();
```

### EntityView

Renders a live preview of an ECS entity's sprite layers (reads its `SpriteComponent`/`TransformComponent`):

```csharp
var preview = new EntityView(playerUid)
{
    Rotation = 0f,
    FollowEntityAngle = true, // ignore Rotation and mirror the entity's own facing instead
};
preview.EntityUid = anotherUid; // swap target
preview.Refresh();               // force a redraw after external changes
```

---

## Misc

| Control | Purpose |
|---|---|
| `TextureRect` | Draws a texture — either `Key` (asset manager atlas key) or a raw `Source`/`SourceRect`. Supports `Rotation`, `Origin`, `Effects`. |
| `ProgressBar` | Read-only fill bar. `Value`/`MaxValue`, `Orientation`, `BarThickness`. |
| `Slider` | Draggable fill bar. `Value`/`MinValue`/`MaxValue`/`Step`, `OnValueChanged`. |
| `ScrollBar` | Draggable thumb over a track. `Value`/`MaxValue`/`Page`, `OnValueChanged`. Used internally by `ScrollContainer`; usable standalone too. |
| `Separator` | A thin line, `Orientation`-aware. |
| `FPSCounter` | A `Label` subclass that updates its own text with the current FPS every frame. |

```csharp
var icon = new TextureRect { Key = "icons_sword_iron", MinWidth = 32, MinHeight = 32 };

var hp = new ProgressBar { Value = 75, MaxValue = 100 };

var volume = new Slider { MinValue = 0, MaxValue = 1, Step = 0.05f };
volume.OnValueChanged += v => SetMasterVolume(v);
```

---

## Window

Covered in [UI — Windows](Ui.md#windows).
