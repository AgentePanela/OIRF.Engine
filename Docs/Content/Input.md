# Input

`InputManager` (accessible via `GameClient.InputManager`) reads the state of keyboard, mouse, and gamepad every frame. It also supports a high-level **action map** system for remappable controls.

---

## Keyboard

All keyboard methods accept a MonoGame `Keys` enum value.

```csharp
var input = GameClient.InputManager;

// True only on the frame the key is first pressed
if (input.KeyPressed(Keys.Space))
    Jump();

// True every frame the key is held down
if (input.KeyDown(Keys.W))
    MoveUp();

// True only on the frame the key is released
if (input.KeyReleased(Keys.Space))
    ReleaseJump();

// True if any key was pressed this frame
if (input.AnyKeyPressed()) { ... }

// True if any key is currently held
if (input.AnyKeyDown()) { ... }

// All currently held keys
Keys[] held = input.PressedKeys;
```

> **Note:** All keyboard methods return `false` when a UI widget has keyboard focus (`UIManager.IsKeyboardFocused == true`).

---

## Mouse

### Position

```csharp
// Position in screen (pixel) space
Vector2 screen = input.MouseScreenPosition;

// Position in world space (accounts for camera and viewport)
Vector2 world = input.MouseWorldPosition;

// Detect mouse movement
var (moved, position) = input.MousePositionChanged();

// Mouse wheel
var (scrolled, delta) = input.MouseWheelDeltaChanged();
// delta > 0 = scrolled up, delta < 0 = scrolled down
```

### Buttons

Use the `MouseButton` enum: `MouseButton.Left`, `MouseButton.Middle`, `MouseButton.Right`.

```csharp
// Clicked = pressed this frame only
if (input.MouseClicked(MouseButton.Left)) { ... }

// Down = held this frame
if (input.MouseDown(MouseButton.Right)) { ... }

// Released = released this frame only
if (input.MouseReleased(MouseButton.Left)) { ... }

// Any button clicked or held
if (input.AnyMouseButtonClicked()) { ... }
if (input.AnyMouseButtonDown()) { ... }
```

---

## Gamepad

Gamepad methods use the MonoGame `Buttons` enum. Only `PlayerIndex.One` is currently supported.

```csharp
// Just pressed
if (input.ButtonPressed(Buttons.A)) { ... }

// Held
if (input.ButtonDown(Buttons.RightShoulder)) { ... }

// Released
if (input.ButtonReleased(Buttons.A)) { ... }

// Thumbstick (stick 0 = left, stick 1 = right)
Vector2 leftStick  = input.GetThumbStickPosition(0);
Vector2 rightStick = input.GetThumbStickPosition(1);
```

---

## Action Map System

The **action map** is a higher-level input layer that maps named actions to one or more bindings (keyboard keys, mouse buttons, or gamepad buttons). This makes input rebindable and keeps game code device-agnostic.

### Defining Actions in YAML

Create an `InputMapPrototype` in your prototypes folder:

```yaml
- type: inputMap
  id: PlayerInputMap
  actions:
    jump:
      keys: [ Space, W ]
      mouse: []
      gamepad: [ A ]
    attack:
      keys: [ Z ]
      mouse: [ Left ]
      gamepad: [ X ]
    moveLeft:
      keys: [ A, Left ]
      mouse: []
      gamepad: []
```

The engine loads all `inputMap` prototypes and merges them into a single action table.

### Using Actions in Code

```csharp
// Just pressed any binding for "jump"
if (input.ActionPressed("jump"))
    Jump();

// Any binding held for "moveLeft"
if (input.ActionDown("moveLeft"))
    MoveLeft();

// Just released any binding for "attack"
if (input.ActionReleased("attack"))
    EndAttack();
```

Action names are **case-insensitive**.

### Rebuilding the Action Table

If you reload prototypes at runtime, call:

```csharp
input.InvalidateActions();
```

This clears the cache; the action table is rebuilt on the next action query.

---

## Integration with UI

When a UI widget (text box, etc.) has keyboard focus, the `InputManager` suppresses all keyboard events to prevent game input from leaking through. Mouse events are unaffected.

```csharp
// Check before processing keyboard input manually
if (!GameClient.InterfaceManager.IsKeyboardFocused)
{
    // safe to read keyboard
}
```
