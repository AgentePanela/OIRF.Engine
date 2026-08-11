using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Client.UI.Controls;
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

    public PanelContainer Root { get; } = new()
    {
        Margin = new(15),
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private ShapeBatch _shapeBatch = default!;

    private static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };

    public void Init()
    {
        IoCManager.ResolveDependencies(this);
        _shapeBatch = new ShapeBatch(GameClient.GraphicsDevice);
        _defaultStyleProto = _protoMan.Index(_defaultStyleId);

        var root = new BoxContainer { Orientation = Orientation.Vertical, _separation = 10 };
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
        _shapeBatch.Begin(rasterizerState: ScissorRasterizer);
        Root.Draw(_shapeBatch, _fontMan, dt);
        _shapeBatch.End();
    }
}
