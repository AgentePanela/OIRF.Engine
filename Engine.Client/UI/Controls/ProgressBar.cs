using System;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// A fill up bar.
/// </summary>
public sealed partial class ProgressBar : PanelContainer
{
    private Orientation _orientation = Orientation.Horizontal;

    public Orientation Orientation
    {
        get => _orientation;
        set => SetLayoutField(ref _orientation, value);
    }

    private float _barThickness = 20f;

    /// <summary>
    /// Thickness on the cross axis (height if horizontal, width if vertical) reported to layout.
    /// </summary>
    public float BarThickness
    {
        get => _barThickness;
        set => SetLayoutField(ref _barThickness, value);
    }

    private float _value;

    public float Value
    {
        get => _value;
        set
        {
            var clamped = MathHelper.Clamp(value, 0f, MaxValue);
            if (clamped == _value)
                return;

            _value = clamped;
            OnValueChanged?.Invoke(_value);
        }
    }

    public float MaxValue { get; set; } = 1f;

    public event Action<float>? OnValueChanged;

    [StyleField("fillColor")]
    private ColorGradient? _fillColor;

    public ProgressBar()
    {
        StyleAliasses.Add("progressBar");
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
        => Orientation == Orientation.Horizontal ? new Vector2(0f, BarThickness) : new Vector2(BarThickness, 0f);

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        base.DrawSelf(sb, fontManager, dt);

        if (FillColor is null || MaxValue <= 0 || Value <= 0)
            return;

        var fraction = Value / MaxValue;
        var panelRect = PanelRect(Bounds); // confine the fill to the background box

        var fillRect = Orientation == Orientation.Horizontal
            ? new Rectangle(panelRect.X, panelRect.Y, (int)(panelRect.Width * fraction), panelRect.Height)
            : new Rectangle(panelRect.X, panelRect.Bottom - (int)(panelRect.Height * fraction), panelRect.Width, (int)(panelRect.Height * fraction));

        if (fillRect.Width <= 0 || fillRect.Height <= 0)
            return;

        sb.FillRectangle(new Vector2(fillRect.X, fillRect.Y), new Vector2(fillRect.Width, fillRect.Height),
            FillColor.Value.Resolve(fillRect), aaSize: AntiAnalising);
    }
}
