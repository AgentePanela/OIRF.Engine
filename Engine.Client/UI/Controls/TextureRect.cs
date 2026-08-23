using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

    private Texture2D? _source;

    /// <summary>
    /// Raw texture to display instead of looking up <see cref="Key"/> in the atlas.
    /// Takes priority over Key when set - useful for debug tooling previewing a texture
    /// that isn't itself an atlas sprite (e.g. a whole atlas page).
    /// </summary>
    public Texture2D? Source
    {
        get => _source;
        set => SetLayoutField(ref _source, value);
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

    /// <summary>
    /// Rotation applied around Origin. Dosen't affect Bounds.
    /// </summary>
    public float Rotation { get; set; }

    /// <summary>
    /// Point this sprite rotates around.
    /// </summary>
    public Vector2? Origin { get; set; }

    /// <summary>
    /// Horizontal/vertical flip.
    /// </summary>
    public SpriteEffects Effects { get; set; } = SpriteEffects.None;

    private Rectangle GetEffectiveRegion(AtlasSprite sprite) => SourceRect is { } sub
        ? new Rectangle(sprite.Region.X + sub.X, sprite.Region.Y + sub.Y, sub.Width, sub.Height)
        : sprite.Region;

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        if (Source is { } tex)
            return SourceRect is { } sub ? new Vector2(sub.Width, sub.Height) : new Vector2(tex.Width, tex.Height);

        if (Key is null || !IoCManager.Resolve<IAssetManager>().GetTexture(Key, out var sprite, out _))
            return Vector2.Zero;

        var region = GetEffectiveRegion(sprite);
        return new Vector2(region.Width, region.Height);
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        if (Source is { } tex)
        {
            var srcRegion = SourceRect ?? new Rectangle(0, 0, tex.Width, tex.Height);
            var srcDrawSize = Stretch ? new Vector2(Bounds.Width, Bounds.Height) : new Vector2(srcRegion.Width, srcRegion.Height);
            sb.Draw(tex, new RectangleF(Bounds.X, Bounds.Y, srcDrawSize.X, srcDrawSize.Y), ToRectF(srcRegion), Tint);
            return;
        }

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
        var drawSize = Stretch
            ? new Vector2(Bounds.Width, Bounds.Height)
            : new Vector2(region.Width, region.Height);

        var destination = new RectangleF(Bounds.X, Bounds.Y, drawSize.X, drawSize.Y);
        if (Rotation == 0f && Origin is null && Effects == SpriteEffects.None)
        {
            sb.Draw(page.Texture, destination, source, Tint);
            return;
        }

        var origin = Origin ?? new Vector2(region.Width / 2f, region.Height / 2f);
        sb.Draw(page.Texture, destination, source, Tint, Rotation, origin, Effects);
    }

    private static MonoGame.Extended.RectangleF ToRectF(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
