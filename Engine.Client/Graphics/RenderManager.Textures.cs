using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics.Fonts;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    public void DrawRaw(AtlasPage page, AtlasSprite sprite, Vector2 position,
        Color color = default, float rotation = 0f, Vector2 origin = default, Vector2 scale = default, SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f)
    {
        _batcher.SetSampler(_commandSampler);
        _batcher.Draw(page.Texture, position, sprite.Region, color, rotation, origin, scale, effects);
    }

    /// <summary>
    /// Draw a standalone Texture2D directly (not from atlas).
    /// </summary>
    public void DrawRawTexture2D(Texture2D texture, Vector2 position,
        Color color = default, float rotation = 0f, Vector2 origin = default, Vector2 scale = default, SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f)
    {
        _batcher.SetSampler(_commandSampler);
        _batcher.Draw(texture, position, texture.Bounds, color, rotation, origin, scale, effects);
    }

    public void DrawQuad(Texture2D texture, Rectangle source, Vector2 position, Color color)
    {
        _batcher.SetSampler(_commandSampler);
        _batcher.Draw(texture, position, source, color, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None);
    }

    /// <summary>
    /// Shared by anything drawing a Sprite2D by key: cached atlas data if SpriteSystem already
    /// resolved it once, otherwise a fresh asset manager lookup. False means the key doesn't
    /// exist (yet)
    /// </summary>

    private bool TryResolveSprite(Sprite2D sprite, out Texture2D texture, out Rectangle region)
    {
        if (sprite.CachedTexture is not null)
        {
            texture = sprite.CachedTexture;
            region = sprite.CachedRegion;
            return true;
        }

        if (!_asset.GetTexture(sprite.Key, out var atlasSpr, out var atlasPage))
        {
            texture = null!;
            region = default;
            return false;
        }

        texture = atlasPage.Texture;
        region = atlasSpr.Region;
        return true;
    }

    public void DrawSprite(Sprite2D sprite, Vector2 position)
    {
        if (!TryResolveSprite(sprite, out var texture, out var region))
            return;

        region.X += (int)sprite.Offset.X;
        region.Y += (int)sprite.Offset.Y;

        _batcher.SetSampler(_commandSampler);
        _batcher.Draw(texture, position, region, sprite.Color, sprite.Rotation, sprite.Origin, sprite.Scale, sprite.Effects);
    }

    public void DrawTexture(RawTexture texture, Vector2 position)
    {
        _batcher.SetSampler(_commandSampler);
        _batcher.Draw(texture.Texture, position, texture.Region, texture.Color, texture.Rotation, texture.Origin,
            texture.Scale, SpriteEffects.None);
    }

    public void DrawNineSlice(NineSlice2D slice, Vector2 position)
    {
        if (!TryResolveSprite(slice.Sprite, out var texture, out var region))
            return;

        var dest = new Rectangle((int)position.X, (int)position.Y, (int)slice.Size.X, (int)slice.Size.Y);

        _batcher.SetSampler(_commandSampler);
        foreach (var patch in NineSlicePatch.Compute(region, slice.Margin, dest))
            _batcher.Draw(texture, patch.Dest, patch.Source, slice.Tint);
    }
}
