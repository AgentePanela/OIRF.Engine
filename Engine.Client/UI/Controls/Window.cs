using System;
using HAlign = Engine.Client.UI.HorizontalAlignment;
using VAlign = Engine.Client.UI.VerticalAlignment;

namespace Engine.Client.UI;

/// <summary>
/// A draggable, resizable floating panel with a title bar and a close button.
/// </summary>
public partial class Window : BoxContainer
{
    private readonly BoxContainer _titleRow;
    private readonly PanelContainer _titleBar;
    private readonly Label _titleLabel;
    private readonly Button _closeButton;

    /// <summary>
    /// Where this windows content goes.
    /// </summary>
    public PanelContainer Contents { get; }

    public string Title
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    /// <summary>
    /// Whether Escape closes this window.
    /// </summary>
    public bool CloseOnEscape { get; set; } = true;

    /// <summary>
    /// Whether the edges and corners can be dragged to resize.
    /// </summary>
    public bool Resizable { get; set; } = true;

    public bool ShowCloseButton
    {
        get => _closeButton.Visible;
        set => _closeButton.Visible = value;
    }

    /// <summary>
    /// How wide the grab zone along each edge is, in pixels.
    /// </summary>
    [StyleField("resizeBorder", 6f)]
    private float? _resizeBorder;

    /// <summary>
    /// Fired after the window is closed and removed.
    /// </summary>
    public event Action<Window>? OnClosed;

    internal WindowManager? Manager;

    public Window()
    {
        Orientation = Orientation.Vertical;
        MouseFilter = MouseFilterMode.Stop;
        StyleAliasses.Add("window");

        _titleLabel = new Label
        {
            VerticalAlignment = VAlign.Center,
            TextVerticalAlign = VAlign.Center,
        };
        _titleLabel.StyleAliasses.Add("windowTitle");
        _titleLabel.StyleAliasses.Add("window");

        _titleBar = new PanelContainer { HorizontalExpand = true };
        _titleBar.StyleAliasses.Add("windowTitleBar");
        _titleBar.StyleAliasses.Add("window");
        _titleBar.AddChild(_titleLabel);

        _closeButton = new Button("X") { VerticalAlignment = VAlign.Stretch };
        _closeButton.StyleAliasses.Add("windowCloseButton");
        _closeButton.Content?.StyleAliasses.Add("windowCloseButton");
        _closeButton.StyleAliasses.Add("window");
        _closeButton.Content?.StyleAliasses.Add("window");
        _closeButton.OnClick += _ => Close();

        _titleRow = new BoxContainer { Orientation = Orientation.Horizontal };
        _titleRow.StyleAliasses.Add("windowTitleRow");
        _titleRow.StyleAliasses.Add("window");
        _titleRow.AddChild(_titleBar);
        _titleRow.AddChild(_closeButton);

        Contents = new PanelContainer { VerticalExpand = true };
        Contents.StyleAliasses.Add("windowContents");
        Contents.StyleAliasses.Add("window");

        base.AddChild(_titleRow);
        base.AddChild(Contents);
    }

    public Window(string title) : this()
    {
        Title = title;
    }

    /// <inheritdoc cref="Control.AddChild(Control)"/>
    public new void AddChild(Control child) => Contents.AddChild(child);

    /// <inheritdoc cref="Control.RemoveChild(Control, bool)"/>
    public new void RemoveChild(Control child, bool dispose = false) => Contents.RemoveChild(child, dispose);

    /// <summary>
    /// Closes this window through whichever manager opened it.
    /// </summary>
    public void Close() => Manager?.Close(this);

    internal void NotifyClosed() => OnClosed?.Invoke(this);
}
