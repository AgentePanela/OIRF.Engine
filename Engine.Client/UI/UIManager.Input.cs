using Engine.Client.Inputs;

namespace Engine.Client.UI;

public sealed partial class UIManager
{
    private Control? _hoveredControl;
    private Control? _focusedControl;
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

    /// <summary>
    /// The control that currently holds keyboard focus, if any.
    /// </summary>
    public Control? FocusedControl => _focusedControl;

    /// <summary>
    /// True when some control currently holds keyboard focus (e.g. a text box being typed
    /// into).
    /// </summary>
    public bool IsKeyboardFocused => _focusedControl is not null;

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
}
