using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Lays children out one after another along a single axis (Horizontal or Vertical), with a
/// fixed gap between them.
/// </summary>
public partial class BoxContainer : PanelContainer
{
    private Orientation _orientation = Orientation.Vertical;

    public Orientation Orientation
    {
        get => _orientation;
        set => SetLayoutField(ref _orientation, value);
    }

    /// <summary>
    /// Gap in pixels between consecutive children. Not applied before the first or after the last.
    /// </summary>
    [StyleField("separation", 0f)]
    private float? _separation;

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var mainTotal = 0f;
        var crossMax = 0f;
        var count = 0;

        foreach (var child in ChildrenList)
        {
            child.Measure(availableSize);

            // an invisible child takes up zero space, but must not claim a Separation gap either
            if (!child.EffectivelyVisible && !child.ReservesSpace)
                continue;

            if (Orientation == Orientation.Horizontal)
            {
                mainTotal += child.DesiredSize.X;
                crossMax = MathHelper.Max(crossMax, child.DesiredSize.Y);
            }
            else
            {
                mainTotal += child.DesiredSize.Y;
                crossMax = MathHelper.Max(crossMax, child.DesiredSize.X);
            }

            count++;
        }

        if (count > 1)
            mainTotal += Separation * (count - 1);

        return Orientation == Orientation.Horizontal
            ? new Vector2(mainTotal, crossMax)
            : new Vector2(crossMax, mainTotal);
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        var available = horizontal ? finalRect.Width : finalRect.Height;

        if (Children.Count == 0)
            return;

        var count = 0;
        var desiredTotal = 0f;
        var expandCount = 0;

        foreach (var child in ChildrenList)
        {
            if (!child.EffectivelyVisible && !child.ReservesSpace)
                continue; // see MeasureCore - doesn't get a Separation gap either

            desiredTotal += horizontal ? child.DesiredSize.X : child.DesiredSize.Y;

            if (horizontal ? child.HorizontalExpand : child.VerticalExpand)
                expandCount++;

            count++;
        }

        if (count > 1)
            desiredTotal += Separation * (count - 1);

        var leftover = MathHelper.Max(0f, available - desiredTotal);
        var sharePerExpand = expandCount > 0 ? leftover / expandCount : 0f;

        float offset = horizontal ? finalRect.X : finalRect.Y;

        foreach (var child in ChildrenList)
        {
            if (!child.EffectivelyVisible && !child.ReservesSpace)
            {
                child.Arrange(Rectangle.Empty);
                continue;
            }

            var mainSize = horizontal ? child.DesiredSize.X : child.DesiredSize.Y;
            var expands = horizontal ? child.HorizontalExpand : child.VerticalExpand;

            if (expands)
                mainSize += sharePerExpand;

            var childRect = horizontal
                ? new Rectangle((int)offset, finalRect.Y, (int)mainSize, finalRect.Height)
                : new Rectangle(finalRect.X, (int)offset, finalRect.Width, (int)mainSize);

            child.Arrange(childRect);

            offset += mainSize + Separation;
        }
    }
}
