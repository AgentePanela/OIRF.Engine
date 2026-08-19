using System;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// A scroll track + thumb along one axis. Used to compose <see cref="ScrollContainer">
/// </summary>
public partial class ScrollBar : PanelContainer
{
    private Orientation _orientation = Orientation.Vertical;

    public Orientation Orientation
    {
        get => _orientation;
        set => SetLayoutField(ref _orientation, value);
    }

    private float _barThickness = 10f;

    /// <summary>
    /// Thickness of the bar itself.
    /// </summary>
    public float BarThickness
    {
        get => _barThickness;
        set => SetLayoutField(ref _barThickness, value);
    }

    /// <summary>
    /// Smallest the thumb is ever drawn, regardless of how small MaxValue implies.
    /// </summary>
    public float MinThumbSize { get; set; } = 20f;

    [StyleField("thumbColor", 0x5AFFFFFFu)]
    private Color? _thumbColor;

    [StyleField("thumbCorner", 0f)]
    private Thickness? _thumbCorner;

    private float _value;
    public float Value
    {
        get => _value;
        set
        {
            var clamped = MathHelper.Clamp(value, 0f, MathHelper.Max(0f, MaxValue - Page));
            if (clamped == _value)
                return;

            _value = clamped;
            InvalidateLayout();
            OnValueChanged?.Invoke(_value);
        }
    }

    /// <summary>
    /// Total scrollable range.
    /// </summary>
    public float MaxValue { get; set; } = 1f;

    /// <summary>
    /// How much of MaxValue is visible at once - also how far a track click pages by.
    /// </summary>
    public float Page { get; set; } = 1f;

    public event Action<float>? OnValueChanged;

    public ScrollBar()
    {
        MouseFilter = MouseFilterMode.Stop;
        StyleAliasses.Add("scrollBar");
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
        => Orientation == Orientation.Vertical
            ? new Vector2(BarThickness, 0f)
            : new Vector2(0f, BarThickness);

    private bool _draggingThumb;
    private float _dragGrabOffset;

    protected internal override void MouseButtonDown(MouseButton button)
    {
        base.MouseButtonDown(button);
        _draggingThumb = false;

        if (button != MouseButton.Left || MaxValue <= 0)
            return;

        var mouse = IoCManager.Resolve<InputManager>().MouseScreenPosition;
        var (trackLength, clickPos) = Orientation == Orientation.Vertical
            ? (Bounds.Height, mouse.Y - Bounds.Y)
            : (Bounds.Width, mouse.X - Bounds.X);

        var thumbLength = GetThumbLength(trackLength);
        var thumbStart = GetThumbStart(trackLength, thumbLength);

        if (clickPos < thumbStart || clickPos > thumbStart + thumbLength)
            return; // not on the thumb - Click() below pages the track instead

        _draggingThumb = true;
        _dragGrabOffset = clickPos - thumbStart;
    }

    protected internal override void MouseMove(Vector2 position)
    {
        if (!_draggingThumb || MaxValue <= 0)
            return;

        var trackLength = Orientation == Orientation.Vertical ? Bounds.Height : Bounds.Width;
        var mousePos = Orientation == Orientation.Vertical ? position.Y - Bounds.Y : position.X - Bounds.X;
        var thumbLength = GetThumbLength(trackLength);
        var range = trackLength - thumbLength;

        var thumbStart = MathHelper.Clamp(mousePos - _dragGrabOffset, 0, range);
        Value = range > 0 ? thumbStart / range * (MaxValue - Page) : 0f;
    }

    protected internal override void Click(MouseButton button)
    {
        base.Click(button);

        // a thumb drag ends in a Click too (release still lands over this control) - it
        // already moved Value itself via MouseMove, so don't also page the track here.
        if (_draggingThumb || button != MouseButton.Left || MaxValue <= 0)
            return;

        var mouse = IoCManager.Resolve<InputManager>().MouseScreenPosition;
        var (trackLength, clickPos) = Orientation == Orientation.Vertical
            ? (Bounds.Height, mouse.Y - Bounds.Y)
            : (Bounds.Width, mouse.X - Bounds.X);

        var thumbLength = GetThumbLength(trackLength);
        var thumbStart = GetThumbStart(trackLength, thumbLength);

        if (clickPos < thumbStart)
            Value -= Page;
        else if (clickPos > thumbStart + thumbLength)
            Value += Page;
    }

    private float GetThumbLength(float trackLength)
        => MathHelper.Max(MinThumbSize, Page / MaxValue * trackLength);

    private float GetThumbStart(float trackLength, float thumbLength)
        => MaxValue - Page > 0 ? Value / (MaxValue - Page) * (trackLength - thumbLength) : 0f;

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        base.DrawSelf(sb, fontManager, dt);

        if (MaxValue <= 0)
            return;

        var trackLength = Orientation == Orientation.Vertical ? Bounds.Height : Bounds.Width;
        var thumbLength = GetThumbLength(trackLength);
        var thumbStart = GetThumbStart(trackLength, thumbLength);

        var thumbRect = Orientation == Orientation.Vertical
            ? new Rectangle(Bounds.X, Bounds.Y + (int)thumbStart, Bounds.Width, (int)thumbLength)
            : new Rectangle(Bounds.X + (int)thumbStart, Bounds.Y, (int)thumbLength, Bounds.Height);

        sb.FillRectangle(
            new Vector2(thumbRect.X, thumbRect.Y),
            new Vector2(thumbRect.Width, thumbRect.Height),
            new ColorGradient(ThumbColor).Resolve(thumbRect), aaSize: AntiAnalising, cornerRadii: (CornerRadii)ThumbCorner);
    }
}
