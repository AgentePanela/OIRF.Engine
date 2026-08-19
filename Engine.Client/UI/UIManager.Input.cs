using Microsoft.Xna.Framework.Input;
using Engine.Client.Inputs;

namespace Engine.Client.UI;

public sealed partial class UIManager
{
    private Control? _hoveredControl;
    private Control? _pressedControl;
    private MouseButton _pressedButton;

    /// <summary>
    /// The control the mouse cursor is currently over, if any.
    /// </summary>
    public Control? HoveredControl => _hoveredControl;

    /// <summary>
    /// True when the mouse cursor is over any UI control.
    /// </summary>
    public bool IsMouseOverUI => _hoveredControl is not null;

    private CursorShape _cursorShape = CursorShape.Arrow;

    private void UpdateCursor()
    {
        var target = _pressedControl ?? _hoveredControl;
        var shape = target?.GetCursorShape(_input.MouseScreenPosition) ?? CursorShape.Arrow;

        if (shape == _cursorShape)
            return;

        _cursorShape = shape;
        Mouse.SetCursor(shape switch
        {
            CursorShape.IBeam => MouseCursor.IBeam,
            CursorShape.Hand => MouseCursor.Hand,
            CursorShape.Crosshair => MouseCursor.Crosshair,
            CursorShape.SizeWE => MouseCursor.SizeWE,
            CursorShape.SizeNS => MouseCursor.SizeNS,
            CursorShape.SizeNWSE => MouseCursor.SizeNWSE,
            CursorShape.SizeNESW => MouseCursor.SizeNESW,
            CursorShape.SizeAll => MouseCursor.SizeAll,
            _ => MouseCursor.Arrow,
        });
    }

    private static readonly MouseButton[] AllMouseButtons =
    [
        MouseButton.Left,
        MouseButton.Middle,
        MouseButton.Right,
    ];

    /// <summary>
    /// Points a tracked reference (hovered/focused/pressed control) at a new control,
    /// unsubscribing from the old one.
    /// </summary>
    private void SetTracked(ref Control? field, Control? value)
    {
        if (field == value)
            return;

        if (field is not null)
            field.Disposed -= OnTrackedControlDisposed;

        field = value;

        if (field is not null)
            field.Disposed += OnTrackedControlDisposed;
    }

    private void OnTrackedControlDisposed(Control control)
    {
        if (_hoveredControl == control)
            SetTracked(ref _hoveredControl, null);

        if (_focusedControl == control)
            SetTracked(ref _focusedControl, null);

        if (_pressedControl == control)
            SetTracked(ref _pressedControl, null);
    }

    /// <summary>
    /// Polls all mouse buttons and dispatches MouseButtonDown/MouseButtonUp/Click to whatever
    /// control is involved.
    /// </summary>
    private void UpdateMouseButtons()
    {
        foreach (var button in AllMouseButtons)
        {
            if (_input.MouseClicked(button))
                HandleMouseDown(button);

            if (_input.MouseReleased(button))
                HandleMouseUp(button);
        }
    }

    private void HandleMouseDown(MouseButton button)
    {
        if (button == MouseButton.Left)
            SetFocus(_hoveredControl is { Focusable: true } ? _hoveredControl : null);

        if (_hoveredControl is null)
            return;

        SetTracked(ref _pressedControl, _hoveredControl);
        _pressedButton = button;

        _pressedControl!.MouseButtonDown(button);
    }

    private void HandleMouseUp(MouseButton button)
    {
        if (_pressedControl is null || button != _pressedButton)
            return;

        var pressed = _pressedControl;
        pressed.MouseButtonUp(button);

        // only counts as a click if the cursor was still over the pressed control on release
        // dragging off it and letting go elsewhere cancels the click.
        if (_hoveredControl == pressed)
            pressed.Click(button);

        SetTracked(ref _pressedControl, null);
    }

    /// <summary>
    /// Forwards mouse movement to whichever control is currently pressed - not whatever's
    /// hovered, so a drag keeps reaching its origin even once the cursor slides off it.
    /// </summary>
    private void UpdateMouseMove()
    {
        var (changed, position) = _input.MousePositionChanged();
        if (!changed || _pressedControl is null)
            return;

        _pressedControl.MouseMove(position);
    }

    /// <summary>
    /// Bubbles a wheel delta up from the hovered control through its ancestors, stopping at
    /// whichever one actually handles it (e.g. a ScrollContainer that still has room to scroll).
    /// </summary>
    private void UpdateMouseWheel()
    {
        var (changed, delta) = _input.MouseWheelDeltaChanged();
        if (!changed || delta == 0)
            return;

        for (var control = _hoveredControl; control is not null; control = control.Parent)
        {
            if (control.MouseWheel(delta))
                return;
        }
    }
}
