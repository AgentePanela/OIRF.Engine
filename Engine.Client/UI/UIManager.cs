using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Engine.Shared.Prototypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.UI;

/// <summary>
/// Manages the game interface, adding windows, UI screens, etc...
/// </summary>
public sealed partial class UIManager
{
    [Dependency] private readonly IFontManager _fontMan = default!;
    [Dependency] private readonly InputManager _input = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IVirtualKeyboard _virtualKeyboard = default!;

    public PanelContainer Root { get; } = new()
    {
        VerticalAlignment = VerticalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = new Color(0, 255, 155, 0.25f)
    };

    private ShapeBatch _shapeBatch = default!;

    private static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };

    public void Init()
    {
        IoCManager.ResolveDependencies(this);
        _shapeBatch = new ShapeBatch(GameClient.GraphicsDevice);
        _defaultStyleProto = _protoMan.Index(_defaultStyleId);
        GameClient.Instance.Window.TextInput += OnTextInput; //ts looks ugly

        var root = new BoxContainer 
        { 
            Orientation = Orientation.Vertical, 
            _separation = 10, 
            Margin = new(15),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AddChild(root);

        var row = new BoxContainer { Orientation = Orientation.Horizontal, _separation = 5 };
        root.AddChild(row);

        for (var i = 0; i < 9; i++)
        {
            var tc = new TestControl();
            if (i is 2 or 5)
                tc.HorizontalExpand = true;

            row.AddChild(tc);
        }

        var column = new BoxContainer { Orientation = Orientation.Vertical, _separation = 5 };
        root.AddChild(column);

        for (var i = 0; i < 4; i++)
            column.AddChild(new TestControl());

        column.AddChild(new Label() { Text = "BoxContainer (Vertical)" });

        column.AddChild(new TextureRect
        {
            Key = "EngineInternal/9Slice",
            NineSliceMargins = new Thickness(32),
            Width = 150,
            Height = 85,
        });

        var btn = new Button
        {
            Text = "Test Button"
        };
        btn.OnClick += _ => Log.Debug("I was clicked!");
        column.AddChild(btn);

        column.AddChild(new CheckButton
        {
            Text = "Check Button"
        });

        column.AddChild(new CheckBox
        {
            Text = "Check Box"
        });

        var progress = new ProgressBar
        {
            Width = 180,
            Value = 0.4f,
        };
        column.AddChild(progress);

        column.AddChild(new Button
        {
            Text = "Disabled Button",
            Disabled = true
        });

        var lineEdit = new LineEdit
        {
            PlaceholderText = "Type something...",
            Width = 180,
        };
        lineEdit.OnTextEntered += text => { Log.Debug($"LineEdit submitted: {text}"); lineEdit.Text = ""; };
        column.AddChild(lineEdit);

        var cc = new CenterContainer
        {
            Background = Color.SkyBlue,
            Margin = new(15, 30),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new (30)
        };
        Root.AddChild(cc);
        cc.AddChild(new TestControl());
        cc.AddChild(new Label
        {
            Text = "CenterContainer",
            Color = Color.Black,
            FontSize = 20,
        });

        var scroll = new ScrollContainer
        {
            Background = new Color(20, 20, 20, 200),
            Width = 220,
            Height = 150,
            Margin = new(15),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Root.AddChild(scroll);

        var scrollContent = new BoxContainer { Orientation = Orientation.Vertical, _separation = 5, Margin = new(5) };
        scroll.AddChild(scrollContent);

        for (var i = 0; i < 20; i++)
            scrollContent.AddChild(new Label { Text = $"Scroll item {i}" });
    }

    public void AddChild(Control control) => Root.AddChild(control);

    public void RemoveChild(Control control) => Root.RemoveChild(control);

    public T? FindControl<T>(string name) where T : Control => Root.FindControl<T>(name);

    /// <summary>
    /// Moves keyboard focus to the given control, or clears it if null.
    /// </summary>
    public void SetFocus(Control? control)
    {
        if (control is not null && !control.Focusable)
            return;

        if (_focusedControl == control)
            return;

        _focusedControl?.SetFocused(false);
        SetTracked(ref _focusedControl, control);
        _focusedControl?.SetFocused(true);
        ResetKeyRepeat();

        if (control is { WantsVirtualKeyboard: true })
            _virtualKeyboard.Show();
        else
            _virtualKeyboard.Hide();
    }

    public void Update(float dt)
    {
        var screenSize = new Vector2(
            GameClient.Graphics.PreferredBackBufferWidth,
            GameClient.Graphics.PreferredBackBufferHeight);

        Root.Measure(screenSize);
        Root.Arrange(new Rectangle(0, 0, (int)screenSize.X, (int)screenSize.Y));

        UpdateHover();
        UpdateMouseButtons();
        UpdateMouseMove();
        UpdateMouseWheel();
        UpdateKeyboard(dt);
    }

    private void UpdateHover()
    {
        var hit = Root.HitTest(_input.MouseScreenPosition);

        if (hit == _hoveredControl)
            return;

        _hoveredControl?.UpdateMouseInside(false);
        SetTracked(ref _hoveredControl, hit);
        _hoveredControl?.UpdateMouseInside(true);
    }

    public void Draw(float dt)
    {
        //GameClient.GraphicsDevice.ScissorRectangle = GameClient.GraphicsDevice.Viewport.Bounds;
        _shapeBatch.Begin(rasterizerState: ScissorRasterizer);
        Root.Draw(_shapeBatch, _fontMan, dt);
        _shapeBatch.End();
    }
}
