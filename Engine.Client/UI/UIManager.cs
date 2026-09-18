using System.Diagnostics;
using System;
using System.Linq;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.IoC;
using Engine.Shared.Prototypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Engine.Client.Scenes;

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
    [Dependency] private readonly SceneManager _sceneMan = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    /// <summary>
    /// Current UI-wide scale factor.
    /// </summary>
    public float UIScale { get; private set; } = 1f;
    private float _manualScale = 1f;

    public PanelContainer Root { get; } = new()
    {
        VerticalAlignment = VerticalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        //Background = new Color(0, 255, 155, 0.25f)
    };

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

        //clears cache
        _protoMan.PrototypesReloaded += (typeKey, _) =>
        {
            if (typeKey.Equals("style", StringComparison.OrdinalIgnoreCase))
                Root.AnnounceThemeUpdate();
        };

        _sceneMan.OnBeforeSceneChange += (old, scene) =>
        {
            if (old is not null && old.Layout is not null)
                RemoveOverlay(old.Layout, true);

            if (scene.Layout is null)
                return;

            AddOverlay(scene.Layout, zindex: 0);
            if (scene.Layout.Name is null)
                scene.Layout.Name = scene.GetType().Name;
        };

        _cfg.Subs(UiCvars.ResAutoScaleEnabled, _ => RecomputeUIScale());
        _cfg.Subs(UiCvars.ResAutoScaleUpperX, _ => RecomputeUIScale());
        _cfg.Subs(UiCvars.ResAutoScaleUpperY, _ => RecomputeUIScale());
        _cfg.Subs(UiCvars.ResAutoScaleLowX, _ => RecomputeUIScale());
        _cfg.Subs(UiCvars.ResAutoScaleLowY, _ => RecomputeUIScale());
        _cfg.Subs(UiCvars.ResAutoScaleMin, _ => RecomputeUIScale());
        _cfg.Subs(UiCvars.Scale, v => { _manualScale = v; RecomputeUIScale(); }, true);
    }

    /// <summary>
    /// Recomputes <see cref="UIScale"/> from the current screen size, the auto-scale cvars and the
    /// manual scale cvar (multiplied together), marking the layout dirty if it changed.
    /// </summary>
    private void RecomputeUIScale()
    {
        var scale = ComputeAutoScale(_lastScreenSize) * MathHelper.Max(0.1f, _manualScale);
        if (scale == UIScale)
            return;

        UIScale = scale;
        _layoutDirty = true;
    }

    private float ComputeAutoScale(Vector2 screenSize)
    {
        if (!_cfg.Get(UiCvars.ResAutoScaleEnabled))
            return 1f;

        var tX = InverseLerpClamped(_cfg.Get(UiCvars.ResAutoScaleLowX), _cfg.Get(UiCvars.ResAutoScaleUpperX), screenSize.X);
        var tY = InverseLerpClamped(_cfg.Get(UiCvars.ResAutoScaleLowY), _cfg.Get(UiCvars.ResAutoScaleUpperY), screenSize.Y);

        return MathHelper.Lerp(_cfg.Get(UiCvars.ResAutoScaleMin), 1f, MathHelper.Min(tX, tY));
    }

    private static float InverseLerpClamped(float a, float b, float v)
    {
        if (MathF.Abs(b - a) < 0.0001f)
            return v >= b ? 1f : 0f;

        return MathHelper.Clamp((v - a) / (b - a), 0f, 1f);
    }

    public void AddChild(Control control) => Root.AddChild(control);

    public void RemoveChild(Control control, bool dispose = false) => Root.RemoveChild(control, dispose);

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
            RecomputeUIScale();
            _layoutDirty = true;
        }

        UIProfiler.BeginUpdate();

        if (_layoutDirty)
        {
            var layoutStart = Stopwatch.GetTimestamp();
            var logicalSize = screenSize / UIScale;
            Root.Measure(logicalSize);
            Root.Arrange(new Rectangle(0, 0, (int)logicalSize.X, (int)logicalSize.Y));
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
        var hit = Root.HitTest(_input.MouseUIPosition);

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
        _shapeBatch.Begin(view: Matrix.CreateScale(UIScale), rasterizerState: ScissorRasterizer);
        Root.Draw(_shapeBatch, _fontMan, dt);
        _shapeBatch.End();
        UIProfiler.EndFrame();
    }

    public void AddOverlay(Overlay overlay, string? name = default, int zindex = 998)
    {
        if (name is null && overlay.Name is null)
            name = Guid.NewGuid().ToString();
        
        overlay.ZIndex = zindex;
        overlay.Name = name;
        AddChild(overlay);
    }

    public T? GetOverlay<T>(string name) where T : Overlay
    {
        return FindControl<T>(name);
    }

    public void RemoveOverlay(Overlay overlay, bool dispose = true)
    {
        RemoveChild(overlay, dispose);
    }
}
