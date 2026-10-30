using System;
using Apos.Shapes;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Shows what a <see cref="RenderViewport"/> sees.
/// </summary>
public partial class ViewportControl : Control
{
    private RenderViewport? _viewport;

    public RenderViewport? Viewport
    {
        get => _viewport;
        set
        {
            if (_viewport == value)
                return;

            _viewport = value;
            if (value is not null && AutoEnable)
                value.Enabled = EffectivelyVisible;

            InvalidateLayout();
        }
    }

    /// <summary>
    /// Make the control be drawed at the size of the viewport.
    /// </summary>
    public bool AutoResize { get; set; } = true;

    /// <summary>
    /// The viewport is only drawn while this control is visible. Set to false when something else also shows it.
    /// </summary>
    public bool AutoEnable { get; set; } = true;

    public Color Tint { get; set; } = Color.White;

    /// <summary>
    /// (Without AutoResize) Scales the frame to fill Bounds instead of showing it at the size of the viewport.
    /// </summary>
    [StyleField("stretch", true)]
    private bool? _stretch;

    [StyleField("keepAspect", true)]
    private bool? _keepAspect;

    /// <summary>
    /// With stretch, rounds the scale down to a whole number so pixel art keeps even pixels.
    /// </summary>
    [StyleField("integerScale", false)]
    private bool? _integerScale;

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        // with AutoResize it is the control that gives the viewport its size, not the other way around
        if (AutoResize || _viewport is null)
            return Vector2.Zero;

        return _viewport.Size;
    }

    protected override void VisibilityChanged(bool newEffectiveVisible)
    {
        base.VisibilityChanged(newEffectiveVisible);

        if (AutoEnable && _viewport is not null)
            _viewport.Enabled = newEffectiveVisible;
    }

    private static float WholeScale(float scale) => scale >= 1f ? MathF.Floor(scale) : scale;

    private MonoGame.Extended.RectangleF GetFrameRect()
    {
        if (AutoResize || _viewport is null || _viewport.Size.X <= 0 || _viewport.Size.Y <= 0)
            return new MonoGame.Extended.RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height);

        var scale = Vector2.One;
        if (Stretch)
        {
            scale = new Vector2(Bounds.Width / _viewport.Size.X, Bounds.Height / _viewport.Size.Y);

            if (KeepAspect)
                scale = new Vector2(MathHelper.Min(scale.X, scale.Y));

            if (IntegerScale)
                scale = new Vector2(WholeScale(scale.X), WholeScale(scale.Y));
        }

        var size = _viewport.Size * scale;

        // centered in whatever room is left
        return new MonoGame.Extended.RectangleF(
            Bounds.X + (Bounds.Width - size.X) / 2f,
            Bounds.Y + (Bounds.Height - size.Y) / 2f,
            size.X, size.Y);
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        if (_viewport is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var uiScale = GameClient.InterfaceManager.UIScale;

        if (AutoResize)
        {
            var size = new Vector2(Bounds.Width, Bounds.Height);
            if (_viewport.Size != size || _viewport.RenderScale != uiScale)
                _viewport.Resize(size, uiScale);
        }

        var rect = GetFrameRect();

        // for the camera own ScreenToWorld / WorldToScreen
        _viewport.ScreenOffset = new Vector2(rect.X, rect.Y) * uiScale;

        // the frame is the one of this same frame: viewports are drawn before the UI
        if (_viewport.Target is not { } frame)
            return;

        sb.Draw(frame, rect, new MonoGame.Extended.RectangleF(0, 0, frame.Width, frame.Height), Tint);
    }

    public Vector2 ScreenToWorld(Vector2 point)
    {
        var rect = GetFrameRect();
        if (_viewport is null || rect.Width <= 0 || rect.Height <= 0)
            return Vector2.Zero;

        var onTarget = (point - new Vector2(rect.X, rect.Y))
                       * new Vector2(_viewport.TargetSize.X / rect.Width, _viewport.TargetSize.Y / rect.Height);

        return Vector2.Transform(onTarget, Matrix.Invert(_viewport.GetViewMatrix()));
    }

    public Vector2 WorldToScreen(Vector2 world)
    {
        if (_viewport is null || _viewport.TargetSize.X <= 0 || _viewport.TargetSize.Y <= 0)
            return Vector2.Zero;

        var rect = GetFrameRect();
        var onTarget = Vector2.Transform(world, _viewport.GetViewMatrix());

        return new Vector2(rect.X, rect.Y)
               + onTarget * new Vector2(rect.Width / _viewport.TargetSize.X, rect.Height / _viewport.TargetSize.Y);
    }
}
