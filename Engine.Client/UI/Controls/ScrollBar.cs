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
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>
    /// Thickness of the bar itself.
    /// </summary>
    public float BarThickness { get; set; } = 10f;

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
        StyleClasses.Add("scrollBar");
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
        => Orientation == Orientation.Vertical
            ? new Vector2(BarThickness, 0f)
            : new Vector2(0f, BarThickness);

    protected internal override void Click(MouseButton button)
    {
        base.Click(button);

        if (button != MouseButton.Left || MaxValue <= 0)
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
