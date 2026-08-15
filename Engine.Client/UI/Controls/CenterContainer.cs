using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Centers each child at its own measured size.
/// </summary>
public partial class CenterContainer : PanelContainer
{
    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var size = Vector2.Zero;

        foreach (var child in Children)
        {
            child.Measure(availableSize);
            size = Vector2.Max(size, child.DesiredSize);
        }

        return size;
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        foreach (var child in Children)
        {
            var width = MathHelper.Min(child.DesiredSize.X, finalRect.Width);
            var height = MathHelper.Min(child.DesiredSize.Y, finalRect.Height);

            var x = finalRect.X + (finalRect.Width - width) / 2f;
            var y = finalRect.Y + (finalRect.Height - height) / 2f;

            child.Arrange(new Rectangle((int)x, (int)y, (int)width, (int)height));
        }
    }
}
