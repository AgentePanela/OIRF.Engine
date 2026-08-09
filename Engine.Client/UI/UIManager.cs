using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Shared.IoC;
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

    public PanelContainer Root { get; } = new();

    private ShapeBatch _shapeBatch = default!;

    private static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };

    public void Init()
    {
        IoCManager.ResolveDependencies(this);
        _shapeBatch = new ShapeBatch(GameClient.GraphicsDevice);
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

        if (FocusedControl == control)
            return;

        FocusedControl?.SetFocused(false);
        FocusedControl = control;
        FocusedControl?.SetFocused(true);
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

        if (hit == HoveredControl)
            return;

        HoveredControl?.UpdateMouseInside(false);
        HoveredControl = hit;
        HoveredControl?.UpdateMouseInside(true);
    }

    public void Draw(float dt)
    {
        _shapeBatch.Begin(rasterizerState: ScissorRasterizer);
        Root.Draw(_shapeBatch, _fontMan, dt);
        _shapeBatch.End();
    }
}
