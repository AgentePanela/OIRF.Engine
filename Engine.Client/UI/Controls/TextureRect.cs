using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace Engine.Client.UI;

/// <summary>
/// Displays a sprite from the game's texture atlas.
/// </summary>
public partial class TextureRect : Control
{
    private string? _key;

    /// <summary>
    /// Atlas sprite key to display.
    /// </summary>
    public string? Key
    {
        get => _key;
        set => SetLayoutField(ref _key, value);
    }

    private Rectangle? _sourceRect;

    /// <summary>
    /// Crops with local cordinates the sprite to this sub-rect instead of showing it whole.
    /// </summary>
    public Rectangle? SourceRect
    {
        get => _sourceRect;
        set => SetLayoutField(ref _sourceRect, value);
    }

    [StyleField("tint", 0xFFFFFFFFu)]
    private Color? _tint;

    /// <summary>
    /// If true, stretches the sprite to fill Bounds instead of drawing it at its native size.
    /// Ignored when <see cref="NineSliceMargins"/> is set.
    /// </summary>
    [StyleField("stretch", false)]
    private bool? _stretch;

    /// <summary>
    /// Cut margins (in source pixels) for the 9-slice grid.
    /// </summary>
    [StyleField("nineSliceMargins")]
    private Thickness? _nineSliceMargins;

    private Rectangle GetEffectiveRegion(AtlasSprite sprite) => SourceRect is { } sub
        ? new Rectangle(sprite.Region.X + sub.X, sprite.Region.Y + sub.Y, sub.Width, sub.Height)
        : sprite.Region;

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        if (Key is null || !IoCManager.Resolve<IAssetManager>().GetTexture(Key, out var sprite, out _))
            return Vector2.Zero;

        var region = GetEffectiveRegion(sprite);
        return new Vector2(region.Width, region.Height);
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        if (Key is null || !IoCManager.Resolve<IAssetManager>().GetTexture(Key, out var sprite, out var page))
            return;

        var region = GetEffectiveRegion(sprite);

        if (NineSliceMargins is { } margins)
        {
            foreach (var patch in NineSlicePatch.Compute(region, margins, Bounds))
                sb.Draw(page.Texture, ToRectF(patch.Dest), ToRectF(patch.Source), Tint);

            return;
        }

        var source = ToRectF(region);
        var destination = Stretch
            ? new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height)
            : new RectangleF(Bounds.X, Bounds.Y, region.Width, region.Height);

        sb.Draw(page.Texture, destination, source, Tint);
    }

    private static MonoGame.Extended.RectangleF ToRectF(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
