# User Interface (UI)

The ORIF Engine uses the **Myra** UI library to build user interfaces. The engine wraps Myra controls in robust manager classes and provides canvas/window abstractions to simplify building menus, HUDs, inventory windows, and overlays.

---

## Overview

| Class | Role |
|-------|------|
| `UIManager` | Manages the main Myra `Desktop` and transitions between screens (`UICanvas`). |
| `WindowManager` | Manages overlapping, draggable game windows (`DefaultWindow`). |
| `UICanvas` | Base class for screen-sized UI layouts (e.g. main menus, HUDs). |
| `DefaultWindow` | Base class for floating, draggable, closable window dialogs. |
| `UITheme` | Provides a flat-color styling interface to repaint default Myra controls. |
| `SpriteWidget` | Custom widget to display a `Sprite2D` texture from the asset manager atlas. |
| `EntityWidget` | Custom widget that renders a visual preview of an ECS entity. |

---

## UI Styling & Themes

`UITheme` allows you to customize the color palette of standard Myra controls (Windows, Buttons, Labels, TextBoxes, Trees, ListBoxes, and Tabs) using flat colors.

To modify the theme, set the properties of `UITheme` and then call `UITheme.ApplyFlatColors()`:

```csharp
using Engine.Client.UI;
using Microsoft.Xna.Framework;

// Configure theme properties
UITheme.BackgroundColor = new Color(30, 30, 35);
UITheme.ButtonColor = new Color(15, 15, 20);
UITheme.ButtonHoverColor = new Color(50, 50, 60);
UITheme.ButtonPressedColor = new Color(70, 70, 80);
UITheme.TextColor = Color.White;
UITheme.SelectionColor = Color.Orange;

// Apply the style changes to Myra
UITheme.ApplyFlatColors();
```

---

## Creating UI Screens (UICanvas)

A `UICanvas` represents a full-screen layout. It automatically handles dependency injection, element assembly, and update/draw loops.

### Creating a Canvas Class

Inherit from `UICanvas` and override `BuildElements` to construct your layout:

```csharp
using Engine.Client.UI;
using Myra.Graphics2D.UI;

public class MainMenuCanvas : UICanvas
{
    private TextButton? _playButton;

    public override void BuildElements()
    {
        // Optional: Change the root layout container from Panel to a stack panel before building
        SetRootType<VerticalStackPanel>();

        var rootStack = (VerticalStackPanel)Root;
        rootStack.Spacing = 10;
        rootStack.HorizontalAlignment = HorizontalAlignment.Center;
        rootStack.VerticalAlignment = VerticalAlignment.Center;

        var title = new Label { Text = "My Game Title", Scale = new Microsoft.Xna.Framework.Vector2(2) };
        _playButton = new TextButton { Text = "Play Game", Width = 200 };

        AddElement(title);
        AddElement(_playButton);

        // ALWAYS call the base BuildElements at the end
        base.BuildElements();
    }

    public override void Initialize()
    {
        base.Initialize();

        // Connect button events
        if (_playButton != null)
        {
            _playButton.Click += (s, e) =>
            {
                // Play logic...
            };
        }
    }

    public override void Update(float dt)
    {
        // Custom update logic...
    }

    public override void OnClose()
    {
        // Cleanup resources or unsubscribe events
    }
}
```

### Displaying a Canvas

To display a canvas, you can assign it as the `DefaultCanvas` of a `Scene`:

```csharp
public class MainMenuScene : Scene
{
    public override void OnSceneStart()
    {
        DefaultCanvas = new MainMenuCanvas();
    }
}
```

Alternatively, you can manually transition the `UIManager`:

```csharp
[Dependency] private readonly UIManager _ui = default!;

// Transition to the new screen
_ui.SetDestinationScreen(new MainMenuCanvas());
```

---

## Creating Windows (DefaultWindow)

`DefaultWindow` provides a wrapper around Myra's `Window` control. It is draggable, closable, and manages its own layout content.

### Creating a Window Class

```csharp
using Engine.Client.UI;
using Myra.Graphics2D.UI;

public class InventoryWindow : DefaultWindow
{
    public InventoryWindow()
    {
        Title = "Player Inventory";
        CloseOnEscape = true; // Automatically closes when pressing Escape
    }

    public override void BuildElements()
    {
        // Layout your window elements
        var mainLayout = new Grid();
        mainLayout.ColumnsProportions.Add(new Proportion(ProportionType.Part, 1.0f));
        
        var placeholderLabel = new Label { Text = "Inventory Slots..." };
        mainLayout.Widgets.Add(placeholderLabel);

        AddElement(mainLayout);

        base.BuildElements();
    }

    public override void OnOpen()
    {
        // Triggered when the window is added to the screen
    }

    public override void OnClose()
    {
        // Triggered when closed or disposed
    }
}
```

### Managing Windows with WindowManager

`WindowManager` handles drawing, updating, opening, and closing multiple window overlays.

```csharp
[Dependency] private readonly WindowManager _windowManager = default!;

// Open a window (creates an instance automatically if not already open)
var invWindow = _windowManager.OpenWindow<InventoryWindow>();

// Or pass an existing instance
var dialog = new InventoryWindow();
_windowManager.OpenWindow(dialog);

// Retrieve an open window instance
var openInv = _windowManager.GetWindow<InventoryWindow>();

// Close a specific window
_windowManager.Close(invWindow);

// Close all open windows
_windowManager.CloseAll();
```

---

## Custom Widgets

### SpriteWidget
A `SpriteWidget` displays a specific sprite from the game's texture atlas using its key.

```csharp
// Load using the key defined in the texture atlas metadata
var itemIcon = new SpriteWidget("icons_sword_iron");

// Modify the key at runtime to change the displayed icon
itemIcon.SpriteKey = "icons_shield_wood";
```

### EntityWidget
`EntityWidget` acts as a view/preview of a specific ECS entity. It automatically reads the entity's `SpriteComponent` and constructs internal `SpriteWidget` layers matching the entity's appearance (including custom sprite layers, visibility, color tinting, and rotation derived from `TransformComponent`).

```csharp
// Create a preview widget for a given entity UID
var preview = new EntityWidget(playerEntityUid);

// Add it to any Myra panel layout
panel.Widgets.Add(preview);

// Change the previewed entity target
preview.SetEntity(anotherEntityUid);
```
