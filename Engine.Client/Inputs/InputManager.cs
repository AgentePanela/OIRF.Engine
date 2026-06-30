using System;
using System.Collections.Generic;
using Engine.Shared.Prototypes;
using Engine.Shared.IoC;
using Engine.Client.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Client.Inputs;

public enum MouseButton
{
    Left,
    Middle,
    Right,
}

public enum InputCallbackSupport
{
    /// <summary>
    /// Both events and commands are disabled; only input-handling functions from InputManager are processed.
    /// </summary>
    ConditionalsOnly,

    /// <summary>
    /// Event-based input extensions are enabled on top of regular input-handling functions; commands are disabled.
    /// </summary>
    UseEvents,

    /// <summary>
    /// Command-based input extenstions are enabled on top of regular input-handling functions; events are disabled.
    /// </summary>
    UseCommands
}

/// <summary>
/// Controls & checks the current input state for mouse, keyboard and gamepad.
/// </summary>
public sealed class InputManager()
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly UIManager _ui = default!;

    private KeyboardState _prevKeyboardState;
    private KeyboardState _keyboardState = Keyboard.GetState();

    private MouseState _prevMouseState;
    private MouseState _mouseState = Mouse.GetState();
    private Vector2 _prevMousePosition, _mousePosition;
    private int _prevMouseWheelDelta, _mouseWheelDelta;

    private GamePadState _prevGamePadState;
    private GamePadState _gamePadState = GamePad.GetState(PlayerIndex.One);
    private Dictionary<string, ResolvedAction>? _actions = new();

    internal void Init()
    {
        IoCManager.ResolveDependencies(this);
        EnsureActionsBuilt();
    }

    internal void Update(bool onFocus)
    {
        _prevKeyboardState = _keyboardState;

        _prevMouseState = _mouseState;
        _prevMousePosition = _mousePosition;
        _prevMouseWheelDelta = _mouseWheelDelta;

        _prevGamePadState = _gamePadState;

        if (!onFocus)
        {
            _keyboardState = new KeyboardState();
            _mouseState = new MouseState();
            return;
        }

        _keyboardState = Keyboard.GetState();

        _mouseState = Mouse.GetState();
        _mousePosition = _mouseState.Position.ToVector2();
        _mouseWheelDelta = _mouseState.ScrollWheelValue;

        _gamePadState = GamePad.GetState(PlayerIndex.One);
    }

    #region Mouse
    public Vector2 MouseScreenPosition 
        => _mousePosition;

    public Vector2 MouseWorldPosition
        => GameClient.Renderer.ScreenToWorld(_mousePosition);

    public (bool Changed, Vector2 Position) MousePositionChanged()
        => (_mousePosition != _prevMousePosition, _mousePosition);

    public (bool Changed, int Delta) MouseWheelDeltaChanged()
        => (_mouseWheelDelta != _prevMouseWheelDelta, _mouseWheelDelta - _prevMouseWheelDelta);

    public bool MouseClicked(MouseButton button) => button switch
    {
        MouseButton.Left => _mouseState.LeftButton == ButtonState.Pressed &&
                            _prevMouseState.LeftButton == ButtonState.Released,
        MouseButton.Middle => _mouseState.MiddleButton == ButtonState.Pressed &&
                            _prevMouseState.MiddleButton == ButtonState.Released,
        MouseButton.Right => _mouseState.RightButton == ButtonState.Pressed &&
                            _prevMouseState.RightButton == ButtonState.Released,
        _ => false,
    };

    public bool MouseDown(MouseButton button) => button switch
    {
        MouseButton.Left => _mouseState.LeftButton == ButtonState.Pressed,
        MouseButton.Middle => _mouseState.MiddleButton == ButtonState.Pressed,
        MouseButton.Right => _mouseState.RightButton == ButtonState.Pressed,
        _ => false,
    };

    public bool MouseReleased(MouseButton button) => button switch
    {
        MouseButton.Left => _mouseState.LeftButton == ButtonState.Released &&
                            _prevMouseState.LeftButton == ButtonState.Pressed,
        MouseButton.Middle => _mouseState.MiddleButton == ButtonState.Released &&
                            _prevMouseState.MiddleButton == ButtonState.Pressed,
        MouseButton.Right => _mouseState.RightButton == ButtonState.Released &&
                            _prevMouseState.RightButton == ButtonState.Pressed,
        _ => false,
    };

    public bool AnyMouseButtonClicked()
    {
        return
            (_mouseState.LeftButton == ButtonState.Pressed &&
            _prevMouseState.LeftButton == ButtonState.Released) ||
            (_mouseState.MiddleButton == ButtonState.Pressed &&
            _prevMouseState.MiddleButton == ButtonState.Released) ||
            (_mouseState.RightButton == ButtonState.Pressed &&
            _prevMouseState.RightButton == ButtonState.Released);
    }

    public bool AnyMouseButtonDown()
    {
        return
            _mouseState.LeftButton == ButtonState.Pressed ||
            _mouseState.MiddleButton == ButtonState.Pressed ||
            _mouseState.RightButton == ButtonState.Pressed;
    }
    #endregion

    #region Keyboard
    public Keys[] PressedKeys => _keyboardState.GetPressedKeys();

    public bool KeyPressed(Keys key)
    {
        if (_ui.IsKeyboardFocused) return false;
        return _keyboardState.IsKeyDown(key) && !_prevKeyboardState.IsKeyDown(key);
    }

    public bool KeyDown(Keys key)
        => _keyboardState.IsKeyDown(key) && !_ui.IsKeyboardFocused;

    public bool KeyReleased(Keys key)
        => !_keyboardState.IsKeyDown(key) && _prevKeyboardState.IsKeyDown(key) && !_ui.IsKeyboardFocused;

    public bool AnyKeyPressed()
    {
        if (_ui.IsKeyboardFocused)
            return false;
        
        foreach (var key in _keyboardState.GetPressedKeys())
        {
            if (!_prevKeyboardState.IsKeyDown(key))
                return true;
        }

        return false;
    }

    public bool AnyKeyDown()
        => _keyboardState.GetPressedKeyCount() > 0 && !_ui.IsKeyboardFocused;
    #endregion

    #region Gamepad
    public Vector2 GetThumbStickPosition(int stick)
        => stick == 0 ? _gamePadState.ThumbSticks.Left : _gamePadState.ThumbSticks.Right;

    public bool ButtonPressed(Buttons button)
        => _gamePadState.IsButtonDown(button) && !_prevGamePadState.IsButtonDown(button);

    public bool ButtonDown(Buttons button)
        => _gamePadState.IsButtonDown(button);

    public bool ButtonReleased(Buttons button)
        => !_gamePadState.IsButtonDown(button) && _prevGamePadState.IsButtonDown(button);
    #endregion

    #region Actions (InputMap)

    /// <summary>
    /// Returns true if any binding for the named action was just pressed this frame.
    /// </summary>
    public bool ActionPressed(string action)
    {
        if (!TryGetAction(action, out var resolved))
            return false;

        foreach (var key in resolved.Keys)
            if (KeyPressed(key)) return true;
        foreach (var mb in resolved.MouseButtons)
            if (MouseClicked(mb)) return true;
        foreach (var btn in resolved.GamepadButtons)
            if (ButtonPressed(btn)) return true;

        return false;
    }

    /// <summary>
    /// Returns true if any binding for the named action is currently held down.
    /// </summary>
    public bool ActionDown(string action)
    {
        if (!TryGetAction(action, out var resolved))
            return false;

        foreach (var key in resolved.Keys)
            if (KeyDown(key)) return true;
        foreach (var mb in resolved.MouseButtons)
            if (MouseDown(mb)) return true;
        foreach (var btn in resolved.GamepadButtons)
            if (ButtonDown(btn)) return true;

        return false;
    }

    /// <summary>
    /// Returns true if any binding for the named action was just released this frame.
    /// </summary>
    public bool ActionReleased(string action)
    {
        if (!TryGetAction(action, out var resolved))
            return false;

        foreach (var key in resolved.Keys)
            if (KeyReleased(key)) return true;
        foreach (var mb in resolved.MouseButtons)
            if (MouseReleased(mb)) return true;
        foreach (var btn in resolved.GamepadButtons)
            if (ButtonReleased(btn)) return true;

        return false;
    }

    private bool TryGetAction(string action, out ResolvedAction resolved)
        => _actions!.TryGetValue(action, out resolved);

    private void EnsureActionsBuilt()
    {
        _actions = new Dictionary<string, ResolvedAction>(StringComparer.OrdinalIgnoreCase);

        foreach (var proto in _proto.EnumerateAll<InputMapPrototype>())
        {
            foreach (var (name, action) in proto.Actions)
            {
                var resolved = new ResolvedAction();

                foreach (var keyName in action.Keys)
                {
                    if (Enum.TryParse<Keys>(keyName, ignoreCase: true, out var key))
                        resolved.Keys.Add(key);
                    else
                        Log.Warn($"InputMap '{proto.ID}': unknown key '{keyName}' in action '{name}'.");
                }

                foreach (var mbName in action.Mouse)
                {
                    if (Enum.TryParse<MouseButton>(mbName, ignoreCase: true, out var mb))
                        resolved.MouseButtons.Add(mb);
                    else
                        Log.Warn($"InputMap '{proto.ID}': unknown mouse button '{mbName}' in action '{name}'.");
                }

                foreach (var btnName in action.Gamepad)
                {
                    if (Enum.TryParse<Buttons>(btnName, ignoreCase: true, out var btn))
                        resolved.GamepadButtons.Add(btn);
                    else
                        Log.Warn($"InputMap '{proto.ID}': unknown gamepad button '{btnName}' in action '{name}'.");
                }

                _actions[name] = resolved;
            }
        }

        Log.Debug($"Built {_actions.Count} input action(s) from InputMap prototypes.");
    }

    /// <summary>
    /// Force rebuild of action bindings. Call after reloading prototypes at runtime.
    /// </summary>
    public void InvalidateActions()
    {
        _actions = null;
    }

    private struct ResolvedAction
    {
        public List<Keys> Keys;
        public List<MouseButton> MouseButtons;
        public List<Buttons> GamepadButtons;

        public ResolvedAction()
        {
            Keys = new List<Keys>();
            MouseButtons = new List<MouseButton>();
            GamepadButtons = new List<Buttons>();
        }
    }

    #endregion
}
