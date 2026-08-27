# User Interface (UI)

The engine has its own retained-mode UI toolkit under `Engine.Client.UI` — a tree of `Control` nodes with Godot-style layout (anchors, containers, `HorizontalExpand`/`VerticalExpand`) and a CSS-like stylesheet system (selectors, specificity, pseudo-classes, YAML `style` prototypes). It does not use Myra; the old Myra-based `UICanvas`/`UITheme`/`DefaultWindow` API described in earlier versions of this page is gone.

See [UI Controls](UIControls.md) for the full reference of every built-in control (buttons, containers, text input, lists, tabs, etc).

---

## Overview

| Class | Role |
|-------|------|
| `Control` | Base class for every node in the UI tree. Owns layout, style, input and drawing. |
| `UIManager` | Owns the UI tree's `Root`, drives layout/update/draw each frame, resolves themes. Accessible via `GameClient.InterfaceManager`. |
| `Overlay` | Base for a full-screen piece of UI drawn above the game world (HUDs, menus, dialogs). |
| `Layout` | An `Overlay` meant to be a scene's own HUD — see [`Scene.Layout`](#wiring-ui-to-a-scene). |
| `Window` | A draggable, resizable floating panel with a title bar and close button. |
| `WindowManager` | Opens, stacks, brings-to-front and closes `Window`s. |
| `StylePrototype` | A YAML-defined stylesheet (`type: style`) — selectors + property values, with inheritance. |
| `StyleFieldAttribute` | Marks a control's backing field as a themeable property; a source generator emits the public property. |

---

## Quick Example

```csharp
using Engine.Client.UI;
using Engine.Shared.IoC;
using HAlign = Engine.Client.UI.HorizontalAlignment;
using VAlign = Engine.Client.UI.VerticalAlignment;

public sealed class ConfirmDialog : Window
{
    [Dependency] private readonly WindowManager _windows = default!;

    public ConfirmDialog() : base("Are you sure?")
    {
        var box = new BoxContainer { Orientation = Orientation.Vertical };
        box.StyleClasses.Add("separation-8"); // arbitrary class, see Styling below

        var label = new Label
        {
            Text = "This cannot be undone.",
            HorizontalAlignment = HAlign.Center,
        };

        var confirm = new Button("Delete") { HorizontalAlignment = HAlign.Center };
        confirm.OnClick += _ => Close();

        box.AddChild(label);
        box.AddChild(confirm);
        AddChild(box);
    }
}

// Open it from anywhere with WindowManager injected:
_windows.Open<ConfirmDialog>();
```

---

## Control Basics

`Control` is `IDisposable` and abstract — every concrete widget (`Label`, `Button`, `BoxContainer`, ...) derives from it, directly or through a base like `PanelContainer`/`BaseButton`.

### Hierarchy

```csharp
parent.AddChild(child);          // child must not already have a parent
parent.RemoveChild(child);        // detaches; pass dispose: true to also Dispose() it
parent.ClearChildren();           // removes and disposes every child

Control? found = parent.FindControl<Label>("HealthLabel"); // recursive, by Name

child.Dispose();                  // disposes the whole subtree, detaches from parent
```

Names only need to be unique among siblings, and are how `FindControl<T>` and `UIManager.FindControl<T>`/`GetOverlay<T>` look things up.

### Visibility

```csharp
control.Visible = false;                 // this control's own wish
bool onScreen = control.EffectivelyVisible; // false if any ancestor is also invisible
control.ReservesSpace = true;            // keep its layout slot even while invisible
control.OnVisibilityChanged += c => { }; // fires on effective visibility change
```

An invisible control (and its whole subtree) is skipped by hit-testing and drawing, and by layout unless `ReservesSpace` is set.

---

## Layout

Layout is a two-pass Measure/Arrange system, the same shape as WPF/Avalonia/Godot's `Control`:

1. **Measure(availableSize)** — top-down. Each control asks its children how big they want to be (`MeasureCore`, overridden per-control), then clamps the result against its own `Width`/`Height`/`Min`/`Max` and stores it in `DesiredSize`.
2. **Arrange(finalRect)** — top-down. Each control is placed inside the rect its parent assigned it (applying `Margin` and alignment), stores the result in `Bounds`, then places its own children/content (`ArrangeCore`).

Both passes run automatically whenever anything calls `InvalidateLayout()` (setting almost any layout-affecting property does this for you) — `UIManager.Update` re-measures/re-arranges from `Root` once per frame at most, only when dirty.

### Layout Properties

All of these are `[StyleField]`s (see [Styling](#styling)) — settable directly in C#, or from a stylesheet, per control instance:

| Property | Default | Meaning |
|---|---|---|
| `Width` / `Height` | `null` (auto) | Explicit size. Null means "size to content". |
| `MinWidth` / `MinHeight` | `0` | Lower clamp on the resolved size. |
| `MaxWidth` / `MaxHeight` | `+Infinity` | Upper clamp on the resolved size. |
| `Margin` | `0` | Space outside `Bounds`, between this control and its siblings/parent. |
| `Padding` | `0` | Space between `Bounds` and this control's own content/children. |
| `HorizontalAlignment` / `VerticalAlignment` | `Stretch` | How the control positions itself in the space its parent gave it (`Left`/`Center`/`Right`/`Stretch`, or `Top`/`Center`/`Bottom`/`Stretch`). |
| `HorizontalExpand` / `VerticalExpand` | `false` | Whether this control claims a share of a `BoxContainer`'s leftover space. |

`Thickness` (used by `Margin`/`Padding`) has `Left`/`Top`/`Right`/`Bottom` and constructors for `(all)`, `(horizontal, vertical)`, `(left, top, right, bottom)`.

### Writing a Custom Container

Override `MeasureCore`/`ArrangeCore` instead of `Measure`/`Arrange` — those are sealed entry points that already handle margin/padding/min/max/alignment for you:

```csharp
public sealed class MyContainer : Control
{
    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        // Measure children, return the size this control's content wants.
        foreach (var child in Children)
            child.Measure(availableSize);

        return /* ... */;
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        // Place children inside finalRect (already reduced by Padding).
        foreach (var child in Children)
            child.Arrange(/* a rect for this child */);
    }
}
```

---

## Styling

Every visual/layout property (`Width`, `background`, `color`, `fontSize`, `zIndex`, ...) is resolved the same way: a local per-instance override if one was set in C#, otherwise a lookup against a stylesheet — CSS's cascade, minus inheritance.

### `[StyleField]`

A control declares a themeable property as a private nullable backing field with `[StyleField]`; a source generator (`StylePropertyGenerator`, in `Engine.Generators`) emits the public property:

```csharp
/// <summary>
/// Fill color for this control's own Bounds.
/// </summary>
[StyleField("background")]
private ColorGradient? _background;

[StyleField("fontSize", 16f)]
private float? _fontSize;
```

Generates (roughly):

```csharp
public float FontSize
{
    get => _fontSize ?? GetStyleProperty("fontSize", 16f);
    set { _fontSize = value; InvalidateLayout(); }
}
```

- First constructor argument is the stylesheet property key (`"fontSize"`).
- Second (optional) argument is the fallback used when neither the instance nor any stylesheet rule sets it. Omit it and the generated property is nullable, returning `null` in that case.
- The XML doc comment on the field is copied onto the generated property.
- The class must be `partial` for the generator to add to it.

### Style Classes

Every control carries three independent string sets, all of which invalidate its resolved-style cache when changed:

| Set | Purpose |
|---|---|
| `StyleClasses` | Arbitrary tags you assign, like a CSS class (`control.StyleClasses.Add("danger")`). |
| `StyleAliasses` | Extra type names a rule's `control:` selector can match, beyond the real C# type. Built-in controls use this so subclasses/composed parts still match a rule for their logical role (e.g. `Button`'s internal `Label` gets `StyleAliasses.Add("button")`). |
| `PseudoClasses` | State the engine manages for you — `hover`, `pressed`, `focus`, `disabled`, `readonly` — added/removed automatically as the control's state changes. |

`StyleIdentifier` is a single unique string, like CSS `#id`.

### Style Prototypes (YAML)

A stylesheet is a `style` prototype under `Resources/Prototypes/`, inheriting like any other prototype (`parent: [...]`, `abstract: true`):

```yaml
- type: style
  id: MyGameTheme
  parent: [EngineDefault]   # inherit the engine's built-in default rules
  rules:
    - class: { control: button }
      properties:
        background: { colorA: "#F5F5F5", colorB: "#DDDDDD", pointA: [0, 0], pointB: [0, 1] }
        outlineColor: "#C4C4C4"
        outlineThickness: 1
        padding: 10

    - class: { control: button, pseudoClass: hover }
      properties:
        outlineColor: "#B0B0B0"

    - class: { control: label, styleClass: danger }
      properties:
        color: "#FF4444"

    - class: { id: HealthLabel }
      properties:
        fontSize: 20
```

Each rule's `class` selector can combine:

| Key | Matches |
|---|---|
| `control` | The control's C# type name (walks base types too), or one of its `StyleAliasses`. Omit for universal (`*`). |
| `styleClass` | A tag in the control's `StyleClasses`. |
| `pseudoClass` | A tag in the control's `PseudoClasses` (`hover`, `pressed`, `focus`, `disabled`, `readonly`, ...). |
| `id` | An exact `StyleIdentifier` match. |

When several rules set the same property for a control, the one with the highest **specificity** wins (`id` > `styleClass`/`pseudoClass` > `control` > universal — same ordering as CSS), same as `ui.yml`'s (the engine's own default theme) rules do for every built-in control.

> Selectors don't yet support combining multiple classes on one selector, or ancestor/child combinators (`.dialog Button`) — see the `TODO`s in `Style/StyleRule.cs`.

### Applying a Theme

```csharp
[Dependency] private readonly UIManager _ui = default!;

_ui.SetMainTheme(new ProtoId<StylePrototype>("MyGameTheme"));
```

This is the theme every control falls back to when nothing in its own ancestor chain overrides it. A subtree can opt into a different stylesheet instead:

```csharp
someContainer.StylesheetOverride = "MyGameTheme"; // resolves via UIManager
```

The engine's own default theme (`EngineDefault`, in `Engine.Shared/EngineResources/Prototypes/ui.yml`) is worth reading end to end as a real-world example — it styles every built-in control.

---

## Input & Focus

```csharp
control.MouseFilter = MouseFilterMode.Stop; // Ignore (default) | Stop | Pass — see below
control.Focusable = true;
_ui.SetFocus(control);   // or null to clear focus
bool focused = control.IsFocused;
```

`MouseFilterMode` controls hit-testing:
- `Ignore` (default) — never hit; children are still tested independently. Most non-interactive controls (`Label`, `PanelContainer`) stay `Ignore`.
- `Stop` — receives the mouse event and blocks anything visually behind it in the same branch.
- `Pass` — receives the event, but still lets whatever is behind it get a chance too.

Override these on a `Control` subclass to react to input (all are `protected internal`):

```csharp
protected internal override void MouseEntered() { }
protected internal override void MouseExited() { }
protected internal override void MouseButtonDown(MouseButton button) { }
protected internal override void MouseButtonUp(MouseButton button) { }
protected internal override void Click(MouseButton button) { }     // full press+release over the same control
protected internal override void MouseMove(Vector2 position) { }   // while this control holds the mouse pressed
protected internal override bool MouseWheel(int delta) { }         // return true to consume it
protected internal override void TextEntered(char character) { }   // while focused
protected internal override void KeyDown(Keys key) { }              // while focused
protected internal override CursorShape GetCursorShape(Vector2 point) => CursorShape.Arrow;
```

`hover`/`pressed`/`focus` pseudo-classes are added/removed for you around these, so a stylesheet rule targeting `pseudoClass: hover` just works without any code on your part.

A control that wants an on-screen keyboard while focused (mobile/gamepad) overrides `WantsVirtualKeyboard => true` — every text input control already does this.

---

## Z-Index & Draw Order

```csharp
control.ZIndex = 10;              // [StyleField], default 0 — higher draws later (on top)
parent.MoveChildToFront(child);   // reorders among same-ZIndex siblings, without touching ZIndex
```

Children are drawn (and hit-tested back-to-front) in `ZIndex` order, insertion order breaking ties. `WindowManager`'s `WindowRoot`, for example, sits above regular UI purely through its stylesheet `zIndex`.

To draw your own visuals, override `DrawSelf` (runs before children, so they render on top):

```csharp
protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
{
    sb.FillRectangle(new Vector2(Bounds.X, Bounds.Y), new Vector2(Bounds.Width, Bounds.Height), Color.Red);
}
```

Set `ClipsContent => true` (override the property) to clip this control's children/drawing to its `Bounds` intersected with any ancestor clip — `ScrollContainer`'s viewport does this, for instance.

---

## UIManager

`UIManager` (`GameClient.InterfaceManager`) owns `Root` — a stretch-anchored `PanelContainer` covering the whole screen — and drives the UI tree every frame (layout, hover/input, `Update`, `Draw`).

```csharp
[Dependency] private readonly UIManager _ui = default!;

_ui.AddChild(myControl);           // parents directly under Root
_ui.RemoveChild(myControl, dispose: true);
T? found = _ui.FindControl<T>("Name");
_ui.SetFocus(myControl);
```

### Overlays

An `Overlay` is meant for whole-screen UI layered above the game world — HUDs, menus, dialogs:

```csharp
_ui.AddOverlay(myOverlay, name: "PauseMenu", zindex: 998);
var pause = _ui.GetOverlay<PauseOverlay>("PauseMenu");
_ui.RemoveOverlay(myOverlay);
```

### Wiring UI to a Scene

`Layout` is an `Overlay` subclass specifically meant to be a scene's own HUD. Declare one on your `Scene` and the engine parents/unparents it under `Root` automatically as the scene starts/ends — no manual `AddOverlay`/`RemoveOverlay` needed:

```csharp
public sealed class GameplayScene : Scene
{
    public override Layout? Layout { get; } = new GameplayHud();
}

public sealed class GameplayHud : Layout
{
    [Dependency] private readonly EntityManager _entManager = default!;

    public GameplayHud()
    {
        // Layout's constructor already resolved [Dependency] fields above for you.
        AddChild(new Label { Text = "HP: 100", HorizontalAlignment = HorizontalAlignment.Left });
    }
}
```

Return `null` for scenes with no HUD (the default, e.g. a loading screen).

---

## Windows

`Window` (`BoxContainer` with a title bar, a close button, and a resizable frame) and `WindowManager` replace the old `DefaultWindow`. A window's own content goes through its `Contents` container, which `Window.AddChild`/`RemoveChild` redirect to automatically — see the [Quick Example](#quick-example) above.

```csharp
[Dependency] private readonly WindowManager _windows = default!;

var w = _windows.Open<InventoryWindow>();  // creates (and IoC-resolves) one if not already open, else brings it to front
_windows.Open(new InventoryWindow());       // or pass an existing instance
InventoryWindow? existing = _windows.GetWindow<InventoryWindow>();
_windows.BringToFront(existing);
_windows.Close(existing);
_windows.CloseAll();
```

Per-window options:

```csharp
window.CloseOnEscape = true;   // default: Escape (without Shift) closes the topmost such window
window.Resizable = true;       // default: edges/corners are draggable
window.ShowCloseButton = true;
window.OnClosed += w => { };
```

`WindowManager` already listens for Escape itself (closing the topmost `CloseOnEscape` window, or every open window with Shift held) — you don't need to wire that up per window.

---

See [UI Controls](UIControls.md) for every concrete control (containers, buttons, text input, lists, tabs, rich text, and more).
