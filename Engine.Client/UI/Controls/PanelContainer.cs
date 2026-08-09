using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Stacks every child in the same rectangle. Each one arranges independently within
/// the space this container was given, using its own alignment to position itself inside it.
/// </summary>
public class PanelContainer : Control
{
    /// <summary>
    /// Fill for this control's own Bounds.
    /// </summary>
    public ColorGradient? Background { get; set; }

    /// <summary>
    /// Border fill drawn around this control's own Bounds.
    /// </summary>
    public ColorGradient? OutlineColor { get; set; }

    /// <summary>
    /// Border thickness in pixels.
    /// </summary>
    public Thickness OutlineThickness { get; set; } = new(1);

    /// <summary>
    /// Amounts of anti-analising this control will receive in rendering.
    /// </summary>
    public float AntiAnalising { get; set; }= 0;

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
            child.Arrange(finalRect);
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        if (Background is not null)
            sb.FillRectangle(new Vector2(Bounds.X, Bounds.Y), new Vector2(Bounds.Width, Bounds.Height), 
                Background.Value.Resolve(Bounds), aaSize: AntiAnalising);

        if (OutlineColor is null)
            return;

        // resolved once against the whole panel Bounds, not per-bar.
        var color = OutlineColor.Value.Resolve(Bounds);
        if (OutlineThickness.Top > 0)
            sb.FillRectangle(new Vector2(Bounds.X - AntiAnalising, Bounds.Y - AntiAnalising), 
                new Vector2(Bounds.Width + AntiAnalising * 2, OutlineThickness.Top + AntiAnalising * 2), color, aaSize: AntiAnalising);

        if (OutlineThickness.Bottom > 0)
            sb.FillRectangle(new Vector2(Bounds.X - AntiAnalising, Bounds.Bottom - OutlineThickness.Bottom - AntiAnalising), 
                new Vector2(Bounds.Width + AntiAnalising * 2, OutlineThickness.Bottom + AntiAnalising * 2), color, aaSize: AntiAnalising);

        if (OutlineThickness.Left > 0)
            sb.FillRectangle(new Vector2(Bounds.X - AntiAnalising, Bounds.Y - AntiAnalising), 
                new Vector2(OutlineThickness.Left + AntiAnalising * 2, Bounds.Height + AntiAnalising * 2), color, aaSize: 0);

        if (OutlineThickness.Right > 0)
            sb.FillRectangle(new Vector2(Bounds.Right - OutlineThickness.Right - AntiAnalising, Bounds.Y - AntiAnalising), 
                new Vector2(OutlineThickness.Right + AntiAnalising * 2, Bounds.Height + AntiAnalising * 2), color, aaSize: AntiAnalising);
    }
}
