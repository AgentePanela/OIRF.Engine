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
    [StyleField("outlineThickness", 1)]
    private Thickness? _outlineThickness;

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
    [StyleField("nineSliceMargins", 4)]
    private Thickness? _nineSliceMargins;

    /// <summary>
    /// Tint applied to the texture.
    /// </summary>
    [StyleField("tint", 0xFFFFFFFFu /* little hack :fire: */)]
    private Color? _tint;

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
        if (TextureKey is not null)
        {
            if (IoCManager.Resolve<IAssetManager>().GetTexture(TextureKey, out var sprite, out var page))
            {
                foreach (var patch in NineSlicePatch.Compute(sprite.Region, NineSliceMargins, Bounds))
                    sb.Draw(page.Texture, ToRectF(patch.Dest), ToRectF(patch.Source), Tint);
            }

            return;
        }

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

    // Apos.Shapes' ShapeBatch.Draw wants MonoGame.Extended's RectangleF.
    private static MonoGame.Extended.RectangleF ToRectF(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
