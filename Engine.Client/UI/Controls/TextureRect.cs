using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace Engine.Client.UI;

/// <summary>
/// Displays a sprite from the game <see cref="IAssetManager"/>.
/// </summary>
public sealed partial class TextureRect : Control
{
    /// <summary>
    /// Atlas sprite key to display.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Crops with local cordinates the sprite to this sub-rect instead of showing it whole.
    /// </summary>
    public Rectangle? SourceRect { get; set; }

    [StyleField("tint", 0xFFFFFFFFu)]
    private Color? _tint;

    /// <summary>
    /// If true, stretches the sprite to fill Bounds instead of drawing it at its native size.
    /// </summary>
    [StyleField("stretch", false)]
    private bool? _stretch;

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
        var source = new RectangleF(region.X, region.Y, region.Width, region.Height);

        var destination = Stretch
            ? new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height)
            : new RectangleF(Bounds.X, Bounds.Y, region.Width, region.Height);

        sb.Draw(page.Texture, destination, source, Tint);
    }
}
