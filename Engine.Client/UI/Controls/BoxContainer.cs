using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Controls;

/// <summary>
/// Lays children out one after another along a single axis (Horizontal or Vertical), with a
/// fixed gap between them.
/// </summary>
public class BoxContainer : PanelContainer
{
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>
    /// Gap in pixels between consecutive children. Not applied before the first or after the last.
    /// </summary>
    public float Separation { get; set; } = 0f;

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var mainTotal = 0f;
        var crossMax = 0f;
        var count = 0;

        foreach (var child in Children)
        {
            child.Measure(availableSize);

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

        var count = Children.Count;
        if (count == 0)
            return;

        var desiredTotal = 0f;
        var expandCount = 0;

        foreach (var child in Children)
        {
            desiredTotal += horizontal ? child.DesiredSize.X : child.DesiredSize.Y;

            if (horizontal ? child.HorizontalExpand : child.VerticalExpand)
                expandCount++;
        }

        if (count > 1)
            desiredTotal += Separation * (count - 1);

        var leftover = MathHelper.Max(0f, available - desiredTotal);
        var sharePerExpand = expandCount > 0 ? leftover / expandCount : 0f;

        float offset = horizontal ? finalRect.X : finalRect.Y;

        foreach (var child in Children)
        {
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
