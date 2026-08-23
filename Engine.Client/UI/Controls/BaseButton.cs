using System;
using System.Linq;
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
            {
                AddChild(_content);
                SyncPseudoClasses(_content);
            }
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

            if (_content is not null)
                SyncPseudoClasses(_content);
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
        StyleAliasses.Add("button"); // style rules can use control: button to match this type
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

        if (_content is not null)
            SyncPseudoClasses(_content);

        if (invokeEvent)
            OnToggled?.Invoke(pressed);
    }

    protected internal override void MouseEntered()
    {
        base.MouseEntered();
        if (_content is not null)
            SyncPseudoClasses(_content);
    }

    protected internal override void MouseExited()
    {
        base.MouseExited();
        if (_content is not null)
            SyncPseudoClasses(_content);
    }

    protected override void FocusChanged(bool focused)
    {
        base.FocusChanged(focused);
        if (_content is not null)
            SyncPseudoClasses(_content);
    }

    /// <summary>
    /// Copies this button's current pseudo-classes (hover/pressed/disabled/focus) onto every
    /// descendant of <paramref name="node"/> that shares a StyleAlias with it - e.g. Text's
    /// auto-generated Label, tagged "button" so `control: button` rules match it too. Without
    /// this, a rule like `{ control: button, pseudoClass: disabled }` never reaches that label's
    /// own Color, since PseudoClasses is only ever set on the button itself.
    /// </summary>
    private void SyncPseudoClasses(Control node)
    {
        if (StyleAliasses.Any(node.StyleAliasses.Contains))
        {
            foreach (var pseudo in PseudoClasses.ToArray())
                node.PseudoClasses.Add(pseudo);

            foreach (var pseudo in node.PseudoClasses.ToArray())
            {
                if (!PseudoClasses.Contains(pseudo))
                    node.PseudoClasses.Remove(pseudo);
            }
        }

        foreach (var child in node.Children)
            SyncPseudoClasses(child);
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
