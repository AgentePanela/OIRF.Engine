using System;
using Engine.Client.Inputs;

namespace Engine.Client.UI;

/// <summary>
/// Reusable button barebones logic.
/// </summary>
public abstract partial class BaseButton : PanelContainer
{
    private Control? _content;

    /// <summary>
    /// This button's single child, if any. Setting it replaces whatever was there before.
    /// </summary>
    public Control? Content
    {
        get => _content;
        set
        {
            if (_content is not null)
                RemoveChild(_content);

            _content = value;

            if (_content is not null)
                AddChild(_content);
        }
    }

    private bool _disabled;

    /// <summary>
    /// While true, this button ignores input entirely (MouseFilter becomes Ignore).
    /// </summary>
    public bool Disabled
    {
        get => _disabled;
        set
        {
            if (_disabled == value)
                return;

            _disabled = value;
            MouseFilter = value ? MouseFilterMode.Ignore : MouseFilterMode.Stop;

            if (value)
                PseudoClasses.Add("disabled");
            else
                PseudoClasses.Remove("disabled");
        }
    }

    /// <summary>
    /// If true, a click flips <see cref="Pressed"/> and it stays that way 
    /// like a switch.
    /// </summary>
    public bool ToggleMode { get; set; }

    private bool _pressed;

    /// <summary>
    /// When the mouse is currently held down over it.
    /// </summary>
    public bool Pressed
    {
        get => _pressed;
        set => SetPressed(value, invokeEvent: false);
    }

    /// <summary>
    /// Fires on a button completed click occours.
    /// </summary>
    public event Action<MouseButton>? OnClick;

    /// <summary>
    /// Fires when Pressed changes as a result of a click (ToggleMode only).
    /// </summary>
    public event Action<bool>? OnToggled;

    protected BaseButton()
    {
        MouseFilter = MouseFilterMode.Stop;
        Focusable = true;
        StyleClasses.Add("button"); // child classes can use button as style ref
    }

    private void SetPressed(bool pressed, bool invokeEvent)
    {
        if (_pressed == pressed)
            return;

        _pressed = pressed;

        if (pressed)
            PseudoClasses.Add("pressed");
        else
            PseudoClasses.Remove("pressed");

        if (invokeEvent)
            OnToggled?.Invoke(pressed);
    }

    protected internal override void MouseButtonDown(MouseButton button)
    {
        base.MouseButtonDown(button);

        if (!ToggleMode)
            SetPressed(true, invokeEvent: false);
    }

    protected internal override void MouseButtonUp(MouseButton button)
    {
        base.MouseButtonUp(button);

        if (!ToggleMode)
            SetPressed(false, invokeEvent: false);
    }

    protected internal override void Click(MouseButton button)
    {
        base.Click(button);

        if (ToggleMode)
            SetPressed(!_pressed, invokeEvent: true);

        OnClick?.Invoke(button);
    }
}
