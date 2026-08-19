using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Common anchor/pivot combinations.
/// </summary>
public enum LayoutPreset
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center,
    CenterTop,
    CenterBottom,

    /// <summary>
    /// Stretched to fill the container on both axes.
    /// </summary>
    Wide,
}

/// <summary>
/// Positions its children freely instead of stacking them, using anchors (a fraction of this
/// container own rect).
/// </summary>
public partial class LayoutContainer : Control
{
    private readonly record struct LayoutData(
        Vector2 AnchorMin,
        Vector2 AnchorMax,
        Vector2 OffsetMin,
        Vector2 OffsetMax,
        Vector2 Pivot,
        bool AutoWidth,
        bool AutoHeight)
    {
        public static readonly LayoutData Default =
            new(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, true, true);
    }

    private readonly Dictionary<Control, LayoutData> _data = new();

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        for (var i = 0; i < Children.Count; i++)
            Children[i].Measure(availableSize);

        return Vector2.Zero;
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (!_data.TryGetValue(child, out var data))
                data = LayoutData.Default;

            var (x, width) = Resolve(
                finalRect.X, finalRect.Width, data.AnchorMin.X, data.AnchorMax.X,
                data.OffsetMin.X, data.OffsetMax.X, data.Pivot.X, data.AutoWidth, child.DesiredSize.X);

            var (y, height) = Resolve(
                finalRect.Y, finalRect.Height, data.AnchorMin.Y, data.AnchorMax.Y,
                data.OffsetMin.Y, data.OffsetMax.Y, data.Pivot.Y, data.AutoHeight, child.DesiredSize.Y);

            child.Arrange(new Rectangle((int)x, (int)y, (int)width, (int)height));
        }
    }

    private static (float Start, float Length) Resolve(
        int rectStart, int rectLength, float anchorMin, float anchorMax,
        float offsetMin, float offsetMax, float pivot, bool auto, float desired)
    {
        var start = rectStart + anchorMin * rectLength + offsetMin;

        if (auto)
            return (start - pivot * desired, desired);

        var end = rectStart + anchorMax * rectLength + offsetMax;
        return (start, MathHelper.Max(0f, end - start));
    }

    private void Set(Control child, LayoutData data)
    {
        if (_data.TryGetValue(child, out var existing) && existing == data)
            return; // no-change guard - see Control.SetLayoutField

        _data[child] = data;
        InvalidateLayout();
    }

    private LayoutData Get(Control child)
        => _data.TryGetValue(child, out var data) ? data : LayoutData.Default;

    public new void RemoveChild(Control child, bool dispose = false)
    {
        _data.Remove(child);
        base.RemoveChild(child, dispose);
    }

    #region style API

    private static LayoutContainer? ContainerOf(Control child) => child.Parent as LayoutContainer;

    /// <summary>
    /// Places the child at an explicit position.
    /// </summary>
    public static void SetPosition(Control child, Vector2 position)
    {
        if (ContainerOf(child) is not { } lc)
            return;

        var data = lc.Get(child);
        var size = data.OffsetMax - data.OffsetMin;
        lc.Set(child, data with
        {
            AnchorMin = Vector2.Zero,
            AnchorMax = Vector2.Zero,
            OffsetMin = position,
            OffsetMax = position + size,
            Pivot = Vector2.Zero,
        });
    }

    /// <summary>
    /// Pins the child to an explicit size.
    /// </summary>
    public static void SetSize(Control child, Vector2 size)
    {
        if (ContainerOf(child) is not { } lc)
            return;

        var data = lc.Get(child);
        lc.Set(child, data with
        {
            AnchorMax = data.AnchorMin,
            OffsetMax = data.OffsetMin + size,
            AutoWidth = false,
            AutoHeight = false,
        });
    }

    /// <summary>
    /// Current position of the child as this container sees it, in pixels from its own origin.
    /// </summary>
    public static Vector2 GetPosition(Control child)
        => ContainerOf(child) is { } lc ? lc.Get(child).OffsetMin : Vector2.Zero;

    /// <summary>
    /// Explicit size of the child, or its measured size on whichever axis is still auto.
    /// </summary>
    public static Vector2 GetSize(Control child)
    {
        if (ContainerOf(child) is not { } lc)
            return child.DesiredSize;

        var data = lc.Get(child);
        return new Vector2(
            data.AutoWidth ? child.DesiredSize.X : data.OffsetMax.X - data.OffsetMin.X,
            data.AutoHeight ? child.DesiredSize.Y : data.OffsetMax.Y - data.OffsetMin.Y);
    }

    public static void SetAnchorPreset(Control child, LayoutPreset preset)
    {
        if (ContainerOf(child) is not { } lc)
            return;
            
        var (anchor, wide) = preset switch
        {
            LayoutPreset.TopLeft => (new Vector2(0f, 0f), false),
            LayoutPreset.TopRight => (new Vector2(1f, 0f), false),
            LayoutPreset.BottomLeft => (new Vector2(0f, 1f), false),
            LayoutPreset.BottomRight => (new Vector2(1f, 1f), false),
            LayoutPreset.Center => (new Vector2(0.5f, 0.5f), false),
            LayoutPreset.CenterTop => (new Vector2(0.5f, 0f), false),
            LayoutPreset.CenterBottom => (new Vector2(0.5f, 1f), false),
            _ => (Vector2.Zero, true), // Wide
        };

        var data = lc.Get(child);
        lc.Set(child, wide
            ? data with
            {
                AnchorMin = Vector2.Zero,
                AnchorMax = Vector2.One,
                OffsetMin = Vector2.Zero,
                OffsetMax = Vector2.Zero,
                Pivot = Vector2.Zero,
                AutoWidth = false,
                AutoHeight = false,
            }
            : data with
            {
                AnchorMin = anchor,
                AnchorMax = anchor,
                OffsetMin = Vector2.Zero,
                Pivot = anchor,
            });
    }

    #endregion
}
