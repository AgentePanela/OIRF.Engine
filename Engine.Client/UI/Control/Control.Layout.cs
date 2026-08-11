using System;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

public abstract partial class Control
{
    /// <summary>
    /// Explicit width.
    /// </summary>
    [StyleField("width")]
    private float? _width;

    /// <summary>
    /// Explicit height.
    /// </summary>
    [StyleField("height")]
    private float? _height;

    /// <summary>
    /// Lower clamp applied to the resolved width.
    /// </summary>
    [StyleField("minWidth", 0f)]
    private float? _minWidth;

    /// <summary>
    /// Lower clamp applied to the resolved height.
    /// </summary>
    [StyleField("minHeight", 0f)]
    private float? _minHeight;

    /// <summary>
    /// Upper clamp applied to the resolved width.
    /// </summary>
    [StyleField("maxWidth", float.PositiveInfinity)]
    private float? _maxWidth;

    /// <summary>
    /// Upper clamp applied to the resolved height.
    /// </summary>
    [StyleField("maxHeight", float.PositiveInfinity)]
    private float? _maxHeight;

    /// <summary>
    /// Space reserved outside this control's own Bounds, between it and its siblings/parent.
    /// </summary>
    [StyleField("margin", 0)]
    private Thickness? _margin;

    /// <summary>
    /// Space between this control's own Bounds and its content/children.
    /// </summary>
    [StyleField("padding", 0)]
    private Thickness? _padding;

    /// <summary>
    /// How this control positions itself horizontally within the space its parent assigned it.
    /// </summary>
    [StyleField("horizontalAlignment", HorizontalAlignment.Stretch)]
    private HorizontalAlignment? _horizontalAlignment;

    /// <summary>
    /// How this control positions itself vertically within the space its parent assigned it.
    /// </summary>
    [StyleField("verticalAlignment", VerticalAlignment.Stretch)]
    private VerticalAlignment? _verticalAlignment;

    /// <summary>
    /// Whether this control claims a share of the leftover horizontal space when its
    /// container has more room than the sum of its children's DesiredSize.
    /// </summary>
    [StyleField("horizontalExpand", false)]
    private bool? _horizontalExpand;

    /// <summary>
    /// Whether this control claims a share of the leftover vertical space when its
    /// container has more room than the sum of its children's DesiredSize.
    /// </summary>
    [StyleField("verticalExpand", false)]
    private bool? _verticalExpand;

    /// <summary>
    /// The size this control wants, computed by the last <see cref="Measure"/> pass. Includes margin.
    /// </summary>
    public Vector2 DesiredSize { get; private set; }

    /// <summary>
    /// The final screen-space rectangle this control was placed in by the last <see cref="Arrange"/> pass.
    /// </summary>
    public Rectangle Bounds { get; private set; }

    /// <summary>
    /// If true, this control keeps its layout space even while invisible.
    /// </summary>
    public bool ReservesSpace { get; set; } = false;

    /// <summary>
    /// Computes <see cref="DesiredSize"/> for this control given the space its parent can offer.
    /// </summary>
    public void Measure(Vector2 availableSize)
    {
        if (!EffectivelyVisible && !ReservesSpace)
        {
            DesiredSize = Vector2.Zero;
            return;
        }

        var marginSize = new Vector2(Margin.Left + Margin.Right, Margin.Top + Margin.Bottom);
        var paddingSize = new Vector2(Padding.Left + Padding.Right, Padding.Top + Padding.Bottom);
        var availableForContent = Vector2.Max(availableSize - marginSize, Vector2.Zero);

        var constrainedWidth = MathHelper.Clamp(Width ?? availableForContent.X, MinWidth, MaxWidth);
        var constrainedHeight = MathHelper.Clamp(Height ?? availableForContent.Y, MinHeight, MaxHeight);

        var innerAvailable = Vector2.Max(new Vector2(constrainedWidth, constrainedHeight) - paddingSize, Vector2.Zero);
        var measured = MeasureCore(innerAvailable) + paddingSize;

        var contentWidth = MathHelper.Clamp(Width ?? measured.X, MinWidth, MaxWidth);
        var contentHeight = MathHelper.Clamp(Height ?? measured.Y, MinHeight, MaxHeight);

        DesiredSize = new Vector2(contentWidth, contentHeight) + marginSize;
    }

    /// <summary>
    /// Measures how much space this control's own content wants, given the space offered
    /// (already reduced by margin and clamped by explicit Width/Height/Min/Max).
    /// </summary>
    protected virtual Vector2 MeasureCore(Vector2 availableSize) => Vector2.Zero;

    /// <summary>
    /// Places this control inside the rectangle its parent assigned it, applying margin
    /// and alignment, and computes the final <see cref="Bounds"/>.
    /// </summary>
    public void Arrange(Rectangle finalRect)
    {
        var contentRect = new Rectangle(
            finalRect.X + Margin.Left,
            finalRect.Y + Margin.Top,
            Math.Max(0, finalRect.Width - Margin.Left - Margin.Right),
            Math.Max(0, finalRect.Height - Margin.Top - Margin.Bottom));

        var contentWidth = MathHelper.Clamp(Width ?? (DesiredSize.X - Margin.Left - Margin.Right), MinWidth, MaxWidth);
        var contentHeight = MathHelper.Clamp(Height ?? (DesiredSize.Y - Margin.Top - Margin.Bottom), MinHeight, MaxHeight);

        var width = HorizontalAlignment == HorizontalAlignment.Stretch
            ? contentRect.Width
            : MathHelper.Min(contentWidth, contentRect.Width);

        var height = VerticalAlignment == VerticalAlignment.Stretch
            ? contentRect.Height
            : MathHelper.Min(contentHeight, contentRect.Height);

        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Left => contentRect.X,
            HorizontalAlignment.Right => contentRect.Right - width,
            HorizontalAlignment.Center => contentRect.X + (contentRect.Width - width) / 2f,
            _ => contentRect.X, // Stretch
        };

        var y = VerticalAlignment switch
        {
            VerticalAlignment.Top => contentRect.Y,
            VerticalAlignment.Bottom => contentRect.Bottom - height,
            VerticalAlignment.Center => contentRect.Y + (contentRect.Height - height) / 2f,
            _ => contentRect.Y, // Stretch
        };

        Bounds = new Rectangle((int)x, (int)y, (int)width, (int)height);

        var innerRect = new Rectangle(
            Bounds.X + Padding.Left,
            Bounds.Y + Padding.Top,
            Math.Max(0, Bounds.Width - Padding.Left - Padding.Right),
            Math.Max(0, Bounds.Height - Padding.Top - Padding.Bottom));

        ArrangeCore(innerRect);
    }

    /// <summary>
    /// Places this control's own content/children inside its final <see cref="Bounds"/>.
    /// The base implementation does nothing.
    /// </summary>
    protected virtual void ArrangeCore(Rectangle finalRect)
    {
    }
}
