using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Stacks every child in the same rectangle. Each one arranges independently within
/// the space this container was given, using its own alignment to position itself inside it.
/// </summary>
public partial class PanelContainer : Control
{
    /// <summary>
    /// Fill color for this control's own Bounds.
    /// </summary>
    [StyleField("background")]
    private ColorGradient? _background;

    /// <summary>
    /// Border fill drawn around this control's own Bounds.
    /// </summary>
    [StyleField("outlineColor")]
    private ColorGradient? _outlineColor;

    /// <summary>
    /// Border thickness in pixels, per side.
    /// </summary>
    [StyleField("outlineThickness", 1f)]
    private Thickness? _outlineThickness;

    /// <summary>
    /// Border margin with the container, positive or negative.
    /// </summary>
    [StyleField("outlineMargin", 0f)]
    private Thickness? _outlineMargin;

    /// <summary>
    /// Amount of anti-aliasing this control receives when rendering.
    /// </summary>
    [StyleField("antiAnalising", 0f)]
    private float? _antiAnalising;

    /// <summary>
    /// Sprite key for a 9-slice or Sprite2D drawed instead of the background.
    /// </summary>
    public string? TextureKey { get; set; }

    /// <summary>
    /// Cut margins (in source pixels) for the 9-slice grid.
    /// </summary>
    [StyleField("nineSliceMargins", 4f)]
    private Thickness? _nineSliceMargins;

    /// <summary>
    /// Tint applied to the texture.
    /// </summary>
    [StyleField("tint", 0xFFFFFFFFu /* little hack :fire: */)]
    private Color? _tint;

    // negative OutlineMargin pushes the outline out past Bounds - reserve that much room during
    // layout so an ancestor's scissor doesn't clip it, instead of drawing straight off the edge.
    protected Thickness OutlineOverflow => new(
        MathHelper.Max(0, -OutlineMargin.Left), MathHelper.Max(0, -OutlineMargin.Top),
        MathHelper.Max(0, -OutlineMargin.Right), MathHelper.Max(0, -OutlineMargin.Bottom));

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var overflow = OutlineOverflow;
        var inner = new Vector2(
            MathHelper.Max(0, availableSize.X - overflow.Left - overflow.Right),
            MathHelper.Max(0, availableSize.Y - overflow.Top - overflow.Bottom));

        var size = Vector2.Zero;
        foreach (var child in ChildrenList)
        {
            child.Measure(inner);
            size = Vector2.Max(size, child.DesiredSize);
        }

        return size + new Vector2(overflow.Left + overflow.Right, overflow.Top + overflow.Bottom);
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        var panelRect = PanelRect(finalRect);
        foreach (var child in ChildrenList)
            child.Arrange(panelRect);
    }

    // Bounds shrunk by OutlineOverflow - the actual background/texture/outline-origin box,
    // as opposed to Bounds itself which also includes the reserved outward-outline space.
    protected Rectangle PanelRect(Rectangle bounds)
    {
        var overflow = OutlineOverflow;
        return new Rectangle(
            bounds.X + (int)overflow.Left, bounds.Y + (int)overflow.Top,
            (int)MathHelper.Max(0, bounds.Width - overflow.Left - overflow.Right),
            (int)MathHelper.Max(0, bounds.Height - overflow.Top - overflow.Bottom));
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        var panelRect = PanelRect(Bounds);

        if (TextureKey is not null)
        {
            if (IoCManager.Resolve<IAssetManager>().GetTexture(TextureKey, out var sprite, out var page))
            {
                foreach (var patch in NineSlicePatch.Compute(sprite.Region, NineSliceMargins, panelRect))
                    sb.Draw(page.Texture, ToRectF(patch.Dest), ToRectF(patch.Source), Tint);
            }
        }
        else if (Background is not null)
        {
            sb.FillRectangle(new Vector2(panelRect.X, panelRect.Y), new Vector2(panelRect.Width, panelRect.Height),
                Background.Value.Resolve(panelRect), aaSize: AntiAnalising);
        }

        if (OutlineColor is null)
            return;

        var left = panelRect.X + OutlineMargin.Left;
        var top = panelRect.Y + OutlineMargin.Top;
        var right = panelRect.Right - OutlineMargin.Right;
        var bottom = panelRect.Bottom - OutlineMargin.Bottom;
        var width = right - left;
        var height = bottom - top;

        // resolved once against the whole panel rect, not per-bar.
        var color = OutlineColor.Value.Resolve(panelRect);
        if (OutlineThickness.Top > 0)
            sb.FillRectangle(new Vector2(left - AntiAnalising, top - AntiAnalising),
                new Vector2(width + AntiAnalising * 2, OutlineThickness.Top + AntiAnalising * 2), color, aaSize: AntiAnalising);

        if (OutlineThickness.Bottom > 0)
            sb.FillRectangle(new Vector2(left - AntiAnalising, bottom - OutlineThickness.Bottom - AntiAnalising),
                new Vector2(width + AntiAnalising * 2, OutlineThickness.Bottom + AntiAnalising * 2), color, aaSize: AntiAnalising);

        if (OutlineThickness.Left > 0)
            sb.FillRectangle(new Vector2(left - AntiAnalising, top - AntiAnalising),
                new Vector2(OutlineThickness.Left + AntiAnalising * 2, height + AntiAnalising * 2), color, aaSize: 0);

        if (OutlineThickness.Right > 0)
            sb.FillRectangle(new Vector2(right - OutlineThickness.Right - AntiAnalising, top - AntiAnalising),
                new Vector2(OutlineThickness.Right + AntiAnalising * 2, height + AntiAnalising * 2), color, aaSize: AntiAnalising);
    }

    // Apos.Shapes' ShapeBatch.Draw wants MonoGame.Extended's RectangleF.
    private static MonoGame.Extended.RectangleF ToRectF(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
