using System.Diagnostics;
using System;
using System.Linq;
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
        //Background = new Color(0, 255, 155, 0.25f)
    };

    /// <summary>
    /// Layer every Window lives in. Its zIndex (set from the stylesheet) keeps it above regular
    /// UI no matter what order things were added to Root in.
    /// </summary>
    public LayoutContainer WindowRoot { get; } = new();

    private ShapeBatch _shapeBatch = default!;
    private Vector2 _lastScreenSize;
    private static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };
    private static bool _layoutDirty = true;

    public void Init()
    {
        IoCManager.ResolveDependencies(this);
        _shapeBatch = new ShapeBatch(GameClient.GraphicsDevice);
        _defaultStyleProto = _protoMan.Index(_defaultStyleId);
        GameClient.Instance.Window.TextInput += OnTextInput; //ts looks ugly
        Root.StyleAliasses.Add("body"); // css lol

        WindowRoot.StyleAliasses.Add("windowRoot");
        Root.AddChild(WindowRoot);

        //clears cache
        _protoMan.PrototypesReloaded += (typeKey, _) =>
        {
            if (typeKey.Equals("style", StringComparison.OrdinalIgnoreCase))
                Root.AnnounceThemeUpdate();
        };
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

        if (screenSize != _lastScreenSize)
        {
            _lastScreenSize = screenSize;
            _layoutDirty = true;
        }

        UIProfiler.BeginUpdate();

        if (_layoutDirty)
        {
            var layoutStart = Stopwatch.GetTimestamp();
            Root.Measure(screenSize);
            Root.Arrange(new Rectangle(0, 0, (int)screenSize.X, (int)screenSize.Y));
            _layoutDirty = false;
            UIProfiler.RecordLayout(Stopwatch.GetTimestamp() - layoutStart);
        }

        UpdateHover();
        UpdateMouseButtons();
        UpdateMouseMove();
        UpdateMouseWheel();
        UpdateCursor();
        UpdateKeyboard(dt);
        Root.UpdateAll(dt);

        UIProfiler.EndUpdate();
    }

    internal static void InvalidateLayout() => _layoutDirty = true;

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
        UIProfiler.BeginFrame();
        _shapeBatch.Begin(rasterizerState: ScissorRasterizer);
        Root.Draw(_shapeBatch, _fontMan, dt);
        _shapeBatch.End();
        UIProfiler.EndFrame();
    }
}
