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

    private readonly ScrollViewport _viewport = new();
    private readonly ScrollBar _vScrollBar = new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly ScrollBar _hScrollBar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom };

    private Vector2 _viewportSize;

    /// <summary>
    /// Current scroll position.
    /// </summary>
    public Vector2 ScrollOffset => new(_hScrollBar.Value, _vScrollBar.Value);

    public Vector2 MaxScrollOffset => Vector2.Max(_viewport.DesiredSize - _viewportSize, Vector2.Zero);

    public ScrollContainer()
    {
        MouseFilter = MouseFilterMode.Pass;

        base.AddChild(_viewport);
        base.AddChild(_vScrollBar);
        base.AddChild(_hScrollBar);
    }

    /// <summary>
    /// Adds a child to the scrollable content area (not the scrollbars).
    /// </summary>
    public new void AddChild(Control child) => _viewport.AddChild(child);

    public new void RemoveChild(Control child, bool dispose = false) => _viewport.RemoveChild(child, dispose);

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var overflow = OutlineOverflow;
        availableSize = new Vector2(
            MathHelper.Max(0, availableSize.X - overflow.Left - overflow.Right),
            MathHelper.Max(0, availableSize.Y - overflow.Top - overflow.Bottom));

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

        _viewport.Measure(constraint);
        var overflowSize = new Vector2(overflow.Left + overflow.Right, overflow.Top + overflow.Bottom);

        // Report zero along whichever axis scrolls
        return new Vector2(
            HorizontalScrollEnabled ? 0f : _viewport.DesiredSize.X,
            VerticalScrollEnabled ? 0f : _viewport.DesiredSize.Y) + overflowSize;
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        finalRect = PanelRect(finalRect);

        var viewportWidth = finalRect.Width;
        var viewportHeight = finalRect.Height;

        if (VerticalScrollEnabled)
            viewportWidth -= (int)_vScrollBar.DesiredSize.X;

        if (HorizontalScrollEnabled)
            viewportHeight -= (int)_hScrollBar.DesiredSize.Y;

        _viewportSize = new Vector2(viewportWidth, viewportHeight);

        if (VerticalScrollEnabled)
        {
            _vScrollBar.MaxValue = _viewport.DesiredSize.Y;
            _vScrollBar.Page = viewportHeight;
            _vScrollBar.Visible = MaxScrollOffset.Y > 0;
            _vScrollBar.Arrange(new Rectangle(
                finalRect.Right - (int)_vScrollBar.DesiredSize.X, finalRect.Y, (int)_vScrollBar.DesiredSize.X, viewportHeight));
        }

        if (HorizontalScrollEnabled)
        {
            _hScrollBar.MaxValue = _viewport.DesiredSize.X;
            _hScrollBar.Page = viewportWidth;
            _hScrollBar.Visible = MaxScrollOffset.X > 0;
            _hScrollBar.Arrange(new Rectangle(
                finalRect.X, finalRect.Bottom - (int)_hScrollBar.DesiredSize.Y, viewportWidth, (int)_hScrollBar.DesiredSize.Y));
        }

        _viewport.ScrollOffset = ScrollOffset;
        _viewport.Arrange(new Rectangle(finalRect.X, finalRect.Y, viewportWidth, viewportHeight));
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
    
    private sealed class ScrollViewport : Control
    {
        public Vector2 ScrollOffset;
        protected internal override bool ClipsContent => true;

        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            var size = Vector2.Zero;
            foreach (var child in ChildrenList)
            {
                child.Measure(availableSize);
                size = Vector2.Max(size, child.DesiredSize);
            }

            return size;
        }

        protected override void ArrangeCore(Rectangle finalRect)
        {
            var contentRect = new Rectangle(
                finalRect.X - (int)ScrollOffset.X,
                finalRect.Y - (int)ScrollOffset.Y,
                (int)MathHelper.Max(finalRect.Width, DesiredSize.X),
                (int)MathHelper.Max(finalRect.Height, DesiredSize.Y));

            foreach (var child in ChildrenList)
                child.Arrange(contentRect);
        }
    }
}
