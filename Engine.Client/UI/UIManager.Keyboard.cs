using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Client.UI;

public sealed partial class UIManager
{
    private Control? _focusedControl;
    
    /// <summary>
    /// The control that currently holds keyboard focus, if any.
    /// </summary>
    public Control? FocusedControl => _focusedControl;

    /// <summary>
    /// True when the focused control captures text input (e.g. LineEdit), and game hotkeys should be suppressed.
    /// Buttons/CheckBoxes etc. are Focusable too (for Enter/Space activation) but don't want typed text, so this
    /// checks WantsVirtualKeyboard rather than just "has focus" - otherwise clicking a checkbox would block movement.
    /// </summary>
    public bool IsKeyboardFocused => _focusedControl?.WantsVirtualKeyboard ?? false;

    private const float KeyRepeatDelay = 0.4f; // held-before-repeat-starts
    private const float KeyRepeatInterval = 0.035f; // time between repeats once started

    private Keys? _repeatKey;
    private float _repeatTimer;

    private void UpdateKeyboard(float dt)
    {
        if (_focusedControl is null)
        {
            _repeatKey = null;
            return;
        }

        foreach (var key in _input.KeysPressedThisFrame())
        {
            _focusedControl.KeyDown(key);
            _repeatKey = key;
            _repeatTimer = KeyRepeatDelay;
        }

        if (_repeatKey is not { } repeating)
            return;

        if (!_input.IsKeyDownRaw(repeating))
        {
            _repeatKey = null;
            return;
        }

        _repeatTimer -= dt;
        if (_repeatTimer > 0f)
            return;

        _focusedControl.KeyDown(repeating);
        _repeatTimer = KeyRepeatInterval;
    }

    private void ResetKeyRepeat() => _repeatKey = null;

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (_focusedControl is null || char.IsControl(e.Character))
            return;

        _focusedControl.TextEntered(e.Character);
    }
}
