using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Clips its content and scrolls it via mouse wheel or the ScrollBar children when it's bigger
/// than the space available. VScroll/HScroll are always present as children (Visible toggles
/// with whether that axis actually has anything to scroll) - same shape as Robust's
/// ScrollContainer, just without drag support yet.
/// </summary>
public partial class ScrollContainer : PanelContainer
{
    [StyleField("verticalScrollEnabled", true)]
    private bool? _verticalScrollEnabled;

    [StyleField("horizontalScrollEnabled", false)]
    private bool? _horizontalScrollEnabled;

    /// <summary>
    /// Pixels scrolled per wheel notch (120 units of raw wheel delta).
    /// </summary>
    public float ScrollSpeed { get; set; } = 50f;

    private readonly ScrollBar _vScrollBar = new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly ScrollBar _hScrollBar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom };

    private Vector2 _contentSize;
    private Vector2 _viewportSize;

    /// <summary
    /// >Current scroll position,
    /// </summary>
    public Vector2 ScrollOffset => new(_hScrollBar.Value, _vScrollBar.Value);

    public Vector2 MaxScrollOffset => Vector2.Max(_contentSize - _viewportSize, Vector2.Zero);

    public ScrollContainer()
    {
        MouseFilter = MouseFilterMode.Pass;

        AddChild(_vScrollBar);
        AddChild(_hScrollBar);
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        if (VerticalScrollEnabled)
        {
            _vScrollBar.Measure(availableSize);
            availableSize.X -= _vScrollBar.DesiredSize.X;
        }

        if (HorizontalScrollEnabled)
        {
            _hScrollBar.Measure(availableSize);
            availableSize.Y -= _hScrollBar.DesiredSize.Y;
        }

        // Unconstrained along wichever axis scrolls
        var constraint = new Vector2(
            HorizontalScrollEnabled ? float.PositiveInfinity : availableSize.X,
            VerticalScrollEnabled ? float.PositiveInfinity : availableSize.Y);

        var size = Vector2.Zero;
        foreach (var child in Children)
        {
            if (child == _vScrollBar || child == _hScrollBar)
                continue;

            child.Measure(constraint);
            size = Vector2.Max(size, child.DesiredSize);
        }

        _contentSize = size;

        // Report zero along whichever axis scrolls
        return new Vector2(
            HorizontalScrollEnabled ? 0f : size.X,
            VerticalScrollEnabled ? 0f : size.Y);
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        var viewportWidth = finalRect.Width;
        var viewportHeight = finalRect.Height;

        if (VerticalScrollEnabled)
            viewportWidth -= (int)_vScrollBar.DesiredSize.X;

        if (HorizontalScrollEnabled)
            viewportHeight -= (int)_hScrollBar.DesiredSize.Y;

        _viewportSize = new Vector2(viewportWidth, viewportHeight);

        if (VerticalScrollEnabled)
        {
            _vScrollBar.MaxValue = _contentSize.Y;
            _vScrollBar.Page = viewportHeight;
            _vScrollBar.Visible = MaxScrollOffset.Y > 0;
            _vScrollBar.Arrange(new Rectangle(
                finalRect.Right - (int)_vScrollBar.DesiredSize.X, finalRect.Y, (int)_vScrollBar.DesiredSize.X, viewportHeight));
        }

        if (HorizontalScrollEnabled)
        {
            _hScrollBar.MaxValue = _contentSize.X;
            _hScrollBar.Page = viewportWidth;
            _hScrollBar.Visible = MaxScrollOffset.X > 0;
            _hScrollBar.Arrange(new Rectangle(
                finalRect.X, finalRect.Bottom - (int)_hScrollBar.DesiredSize.Y, viewportWidth, (int)_hScrollBar.DesiredSize.Y));
        }

        var contentRect = new Rectangle(
            finalRect.X - (int)ScrollOffset.X,
            finalRect.Y - (int)ScrollOffset.Y,
            (int)MathHelper.Max(viewportWidth, _contentSize.X),
            (int)MathHelper.Max(viewportHeight, _contentSize.Y));

        // Content is arranged at its own full desired size, 
        // potentially far bigger than the viewport
        foreach (var child in Children)
        {
            if (child == _vScrollBar || child == _hScrollBar)
                continue;

            child.Arrange(contentRect);
        }
    }

    protected internal override bool MouseWheel(int delta)
    {
        var before = ScrollOffset;
        var pixels = delta / 120f * ScrollSpeed;

        if (VerticalScrollEnabled)
            _vScrollBar.Value -= pixels;
        else if (HorizontalScrollEnabled)
            // Fallback: a vertical wheel scrolls horizontally when there's no vertical
            // scrolling to speak of at all (not just maxed out - Value's own clamping handles that).
            _hScrollBar.Value -= pixels;

        return ScrollOffset != before;
    }
}
