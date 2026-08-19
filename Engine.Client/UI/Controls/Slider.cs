using System;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

public partial class Slider : PanelContainer
{
    private Orientation _orientation = Orientation.Horizontal;

    public Orientation Orientation
    {
        get => _orientation;
        set => SetLayoutField(ref _orientation, value);
    }

    private float _barThickness = 20f;

    /// <summary>
    /// Thickness on the cross axis reported to layout.
    /// </summary>
    public float BarThickness
    {
        get => _barThickness;
        set => SetLayoutField(ref _barThickness, value);
    }

    private float _minValue;
    private float _maxValue = 1f;

    public float MinValue
    {
        get => _minValue;
        set => SetLayoutField(ref _minValue, value);
    }

    public float MaxValue
    {
        get => _maxValue;
        set => SetLayoutField(ref _maxValue, value);
    }

    /// <summary>
    /// Snaps Value to multiples of this when set. 0 disables snapping.
    /// </summary>
    public float Step { get; set; } = 0f;

    private float _value;

    public float Value
    {
        get => _value;
        set
        {
            var clamped = MathHelper.Clamp(Snap(value), MinValue, MaxValue);
            if (clamped == _value)
                return;

            _value = clamped;
            InvalidateLayout();
            OnValueChanged?.Invoke(_value);
        }
    }

    public event Action<float>? OnValueChanged;

    [StyleField("trackColor", 0x333333FFu)]
    private Color? _trackColor;

    [StyleField("trackThickness", 4f)]
    private float? _trackThickness;

    [StyleField("trackCorner", 0f)]
    private Thickness? _trackCorner;

    /// <summary>
    /// Optional color for the filled portion of the track.
    /// </summary>
    [StyleField("fillColor")]
    private ColorGradient? _fillColor;

    [StyleField("grabberColor", 0xFFFFFFFFu)]
    private Color? _grabberColor;

    [StyleField("grabberSize", 16f)]
    private Thickness? _grabberSize;

    [StyleField("grabberCorner", 0f)]
    private Thickness? _grabberCorner;

    /// <summary>
    /// Sprite key for the grabber.
    /// </summary>
    [StyleField("grabberKey")]
    private string? _grabberKey;

    private readonly TextureRect _grabberIcon;

    public Slider()
    {
        MouseFilter = MouseFilterMode.Stop;
        StyleAliasses.Add("slider");

        _grabberIcon = new TextureRect { Stretch = true, Visible = false };
        AddChild(_grabberIcon);
    }

    private float Snap(float value) => Step > 0 ? MathF.Round(value / Step) * Step : value;

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        _grabberIcon.Measure(new Vector2(GrabberSize.Left, GrabberSize.Top));
        return Orientation == Orientation.Horizontal ? new Vector2(0f, BarThickness) : new Vector2(BarThickness, 0f);
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        _grabberIcon.Visible = GrabberKey is not null;
        if (GrabberKey is null)
            return;

        _grabberIcon.Key = GrabberKey;
        _grabberIcon.Arrange(ComputeGrabberRect(PanelRect(Bounds)));
    }

    private Rectangle ComputeGrabberRect(Rectangle rect)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        var range = MaxValue - MinValue;
        var fraction = range > 0 ? (Value - MinValue) / range : 0f;

        var grabberCenter = horizontal
            ? rect.X + fraction * rect.Width
            : rect.Bottom - fraction * rect.Height;

        var grabberWidth = GrabberSize.Left;
        var grabberHeight = GrabberSize.Top;

        return horizontal
            ? new Rectangle((int)(grabberCenter - grabberWidth / 2), rect.Y + (int)(rect.Height - grabberHeight) / 2, (int)grabberWidth, (int)grabberHeight)
            : new Rectangle(rect.X + (int)(rect.Width - grabberWidth) / 2, (int)(grabberCenter - grabberHeight / 2), (int)grabberWidth, (int)grabberHeight);
    }

    private bool _dragging;

    protected internal override void MouseButtonDown(MouseButton button)
    {
        base.MouseButtonDown(button);

        if (button != MouseButton.Left)
            return;

        _dragging = true;
        PseudoClasses.Add("pressed");
        SetValueFromMouse(IoCManager.Resolve<InputManager>().MouseScreenPosition);
    }

    protected internal override void MouseMove(Vector2 position)
    {
        if (!_dragging)
            return;

        SetValueFromMouse(position);
    }

    protected internal override void MouseButtonUp(MouseButton button)
    {
        base.MouseButtonUp(button);

        if (button != MouseButton.Left)
            return;

        _dragging = false;
        PseudoClasses.Remove("pressed");
    }

    private void SetValueFromMouse(Vector2 position)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        var trackLength = horizontal ? Bounds.Width : Bounds.Height;
        if (trackLength <= 0)
            return;

        // vertical goes bottom-to-top
        var pos = horizontal ? position.X - Bounds.X : Bounds.Bottom - position.Y;
        var fraction = MathHelper.Clamp(pos / trackLength, 0f, 1f);

        Value = MinValue + fraction * (MaxValue - MinValue);
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        base.DrawSelf(sb, fontManager, dt);

        var rect = PanelRect(Bounds);
        var horizontal = Orientation == Orientation.Horizontal;
        var trackRect = horizontal
            ? new Rectangle(rect.X, rect.Y + (rect.Height - (int)TrackThickness) / 2, rect.Width, (int)TrackThickness)
            : new Rectangle(rect.X + (rect.Width - (int)TrackThickness) / 2, rect.Y, (int)TrackThickness, rect.Height);

        sb.FillRectangle(new Vector2(trackRect.X, trackRect.Y), new Vector2(trackRect.Width, trackRect.Height),
            TrackColor, aaSize: AntiAnalising, cornerRadii: (CornerRadii)TrackCorner);

        if (FillColor is not null)
        {
            var range = MaxValue - MinValue;
            var fraction = range > 0 ? (Value - MinValue) / range : 0f;

            var fillRect = horizontal
                ? new Rectangle(trackRect.X, trackRect.Y, (int)(trackRect.Width * fraction), trackRect.Height)
                : new Rectangle(trackRect.X, trackRect.Bottom - (int)(trackRect.Height * fraction), trackRect.Width, (int)(trackRect.Height * fraction));

            if (fillRect.Width > 0 && fillRect.Height > 0)
                sb.FillRectangle(new Vector2(fillRect.X, fillRect.Y), new Vector2(fillRect.Width, fillRect.Height),
                    FillColor.Value.Resolve(fillRect), aaSize: AntiAnalising, cornerRadii: (CornerRadii)TrackCorner);
        }

        if (GrabberKey is not null)
            return;

        var grabberRect = ComputeGrabberRect(rect);
        sb.FillRectangle(new Vector2(grabberRect.X, grabberRect.Y), new Vector2(grabberRect.Width, grabberRect.Height),
            GrabberColor, aaSize: AntiAnalising, cornerRadii: (CornerRadii)GrabberCorner);
    }
}
