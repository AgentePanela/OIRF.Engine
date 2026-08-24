using System;
using Engine.Client.Inputs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Engine.Client.UI;

public abstract partial class Control : IDisposable
{
    /// <summary>
    /// Whether this control can receive keyboard focus.
    /// </summary>
    public bool Focusable { get; set; }

    /// <summary>
    /// Whether this control currently holds keyboard focus.
    /// </summary>
    public bool IsFocused { get; private set; }

    /// <summary>
    /// Sets the keyboard focus state. Meant to be called by whatever tracks focus tree-wide
    /// not by control code itself!
    /// </summary>
    internal void SetFocused(bool focused)
    {
        if (focused && !Focusable)
            return;

        if (IsFocused == focused)
            return;

        IsFocused = focused;

        if (focused)
            PseudoClasses.Add("focus");
        else
            PseudoClasses.Remove("focus");

        FocusChanged(focused);
    }

    /// <summary>
    /// Called when this control gains or loses keyboard focus.
    /// </summary>
    protected virtual void FocusChanged(bool focused)
    {
    }

    /// <summary>
    /// Whether this control needs an on-screen keyboard shown while it's focused (mobile/gamepad).
    /// </summary>
    protected internal virtual bool WantsVirtualKeyboard => false;

    private bool _mouseInside;

    /// <summary>
    /// Whether the mouse cursor is currently over this control specifically (not a descendant).
    /// </summary>
    public bool IsMouseInside => _mouseInside;

    /// <summary>
    /// Controls whether and how this control participates in mouse hit-testing.
    /// </summary>
    public MouseFilterMode MouseFilter { get; set; } = MouseFilterMode.Ignore;

    /// <summary>
    /// Finds the topmost control (including this one) whose bounds contain the given
    /// screen-space point and whose <see cref="MouseFilter"/> isn't Ignore. Invisible
    /// controls and their whole subtree are never hit.
    /// </summary>
    public Control? HitTest(Vector2 point) => HitTest(point, null);
    
    private Control? HitTest(Vector2 point, Rectangle? ancestorClip)
    {
        if (!EffectivelyVisible)
            return null;

        if (ancestorClip is { } clip && !clip.Contains(point.ToPoint()))
            return null;

        var childClip = ClipsContent
            ? (ancestorClip is { } c ? Rectangle.Intersect(c, Bounds) : Bounds)
            : ancestorClip;

        var ordered = OrderedChildren;
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].HitTest(point, childClip) is { } hit)
                return hit;
        }

        if (MouseFilter == MouseFilterMode.Ignore)
            return null;

        return Bounds.Contains(point.ToPoint()) ? this : null;
    }

    /// <summary>
    /// Called by the input router when hover state changes for this control - fires
    /// <see cref="MouseEntered"/>/<see cref="MouseExited"/> and updates <see cref="IsMouseInside"/>.
    /// </summary>
    internal void UpdateMouseInside(bool inside)
    {
        if (_mouseInside == inside)
            return;

        _mouseInside = inside;

        if (inside)
        {
            PseudoClasses.Add("hover");
            MouseEntered();
        }
        else
        {
            PseudoClasses.Remove("hover");
            MouseExited();
        }
    }

    /// <summary>
    /// Cursor this control wants while the pointer is over it at <paramref name="point"/>
    /// </summary>
    protected internal virtual CursorShape GetCursorShape(Vector2 point) => CursorShape.Arrow;

    protected internal virtual void MouseEntered()
    {
    }

    protected internal virtual void MouseExited()
    {
    }

    protected internal virtual void MouseButtonDown(MouseButton button)
    {
    }

    protected internal virtual void MouseButtonUp(MouseButton button)
    {
    }

    protected internal virtual void Click(MouseButton button)
    {
    }

    /// <summary>
    /// Called every frame the mouse moves while this control is the one holding it pressed -
    /// even once the cursor has moved outside its own <see cref="Bounds"/>.
    /// </summary>
    protected internal virtual void MouseMove(Vector2 position)
    {
    }

    protected internal virtual bool MouseWheel(int delta) => false;

    /// <summary>
    /// Called for each character typed while this control holds keyboard focus
    /// </summary>
    protected internal virtual void TextEntered(char character)
    {
    }

    /// <summary>
    /// Called for each non-character key pressed while this control holds keyboard focus
    /// </summary>
    protected internal virtual void KeyDown(Keys key)
    {
    }
}
