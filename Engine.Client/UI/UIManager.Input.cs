using Engine.Client.Inputs;

namespace Engine.Client.UI;

public sealed partial class UIManager
{
    /// <summary>
    /// The control that currently holds keyboard focus, if any.
    /// </summary>
    public Control? FocusedControl { get; private set; }

    /// <summary>
    /// True when some control currently holds keyboard focus (e.g. a text box being typed
    /// into)
    /// </summary>
    public bool IsKeyboardFocused => FocusedControl is not null;

    /// <summary>
    /// The control the mouse cursor is currently over, if any.
    /// </summary>
    public Control? HoveredControl { get; private set; }

    /// <summary>
    /// True when the mouse cursor is over any UI control.
    /// </summary>
    public bool IsMouseOverUI => HoveredControl is not null;

    private static readonly MouseButton[] AllMouseButtons =
    [
        MouseButton.Left,
        MouseButton.Middle,
        MouseButton.Right,
    ];

    private Control? _pressedControl;
    private MouseButton _pressedButton;

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
            SetFocus(HoveredControl is { Focusable: true } ? HoveredControl : null);

        if (HoveredControl is null)
            return;

        _pressedControl = HoveredControl;
        _pressedButton = button;

        _pressedControl.MouseButtonDown(button);
    }

    private void HandleMouseUp(MouseButton button)
    {
        if (_pressedControl is null || button != _pressedButton)
            return;

        _pressedControl.MouseButtonUp(button);

        // only counts as a click if the cursor was still over the pressed control on release
        // dragging off it and letting go elsewhere cancels the click.
        if (HoveredControl == _pressedControl)
            _pressedControl.Click(button);

        _pressedControl = null;
    }
}
