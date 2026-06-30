using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Assets.Atlas;

internal sealed class AtlasBuilder
{
    private const uint Padding = 2;
    public uint MaxAtlasSize = (uint)AtlasSize.Size2048;
    private readonly bool AcceptDedicatedAtlas;

    internal List<AtlasPage> pages = [];
    internal Dictionary<string, AtlasSprite> sprites = [];
    private readonly List<PendingSprite> _pending = new();
    
    private GraphicsDevice _graphics;
    private SpriteBatch _sb;

    internal AtlasBuilder(bool dedicatedAtlas = true)
    {
        AcceptDedicatedAtlas = dedicatedAtlas;
    }

    private struct PendingSprite
    {
        public string Key;
        public Texture2D Texture;

        public PendingSprite(string key, Texture2D texture)
        {
            Key = key;
            Texture = texture;
        }
    }

    internal void Init(GraphicsDevice graphics, SpriteBatch spriteBatch)
    {
        _graphics = graphics;
        _sb = spriteBatch;
        //Log.Debug("Initing AtlasBuilder...");
        MaxAtlasSize = (uint)GameClient.Options.TextureAtlasSize; //getMaxAtlasSize();
        Log.Debug($"Max atlas size: {MaxAtlasSize}");
        pages.Clear();
        sprites.Clear();
    }

    private uint getMaxAtlasSize()
    {
        var prof = _graphics.GraphicsProfile;
        if (prof == GraphicsProfile.HiDef)
            return (uint)AtlasSize.Size4096;
        else if (prof == GraphicsProfile.Reach)
            return (uint)AtlasSize.Size2048;
        return (uint)AtlasSize.Size1024;
    }

    /// <summary>
    /// Add the sprite to the sprite queue to be baked in a <code>BakeAll()</code>.
    /// </summary>
    /// <param name="key">Used to get the sprite from GetSprite. E.g: Machines/Computer</param>
    /// <param name="source">Sprite Texture2D</param>
    internal void QueueSprite(string key, Texture2D source)
    {
        _pending.Add(new PendingSprite(key, source));
    }

    /// <summary>
    /// Bake all sprites in queue and create the atlas(es).
    /// </summary>
    internal void BakeAll()
    {
        Log.Debug("Baking queue sprites...");
        // by height
        _pending.Sort((a, b) =>
            b.Texture.Height.CompareTo(a.Texture.Height)
        );

        foreach (var sprite in _pending)
        {
            AddSprite(sprite.Key, sprite.Texture);
            sprite.Texture.Dispose(); // please get rid of this bullshit
        }

        _pending.Clear();
    }

    public void AddSprite(string key, Texture2D source)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (page.TryPlace(
                source.Width,
                source.Height,
                Padding,
                out var region
            ))
            {
                page.Regions[key] = region;
                sprites[key] = new AtlasSprite(i, region,
                    source.Width,
                    source.Height
                );

                Copy(source, page, region);
                return;
            }
        }

        // create a new atlas page
        var newPage = new AtlasPage(MaxAtlasSize);

        // verify if the size is valid
        if (!newPage.TryPlace(
            source.Width,
            source.Height,
            Padding,
            out var newRegion
        ))
        {
            if (!AcceptDedicatedAtlas)
                throw new Exception($"Sprite '{key}' is too big to be in a atlas!");
            // sprite is too big, it need it ouwn page.
            uint size = (uint)Math.Max(
                source.Width + Padding * 2,
                source.Height + Padding * 2
            );

            newPage = new AtlasPage(size);

            if (!newPage.TryPlace(
                source.Width,
                source.Height,
                Padding,
                out newRegion
            ))
                throw new Exception($"Sprite '{key}' is too big even for dedicated atlas!");
            //newPage.Locked = true;
        }

        pages.Add(newPage);
        int pageIndex = pages.Count - 1;

        newPage.Regions[key] = newRegion;
        sprites[key] = new AtlasSprite(pageIndex, newRegion, source.Width, source.Height);

        Copy(source, newPage, newRegion);
    }

    public void ReplaceSprite(AtlasSprite spr, Texture2D tex)
    {
        var page = pages[spr.Page];
        Copy(tex, page, spr.Region);
    }

    public void RemoveSpriteRef(string key)
    {
        if (!sprites.TryGetValue(key, out var spr))
            return;

        sprites.Remove(key);
        var page = pages[spr.Page];
        page.Regions.Remove(key); // free the region
    }

    private void Copy(Texture2D src, AtlasPage page, Rectangle region)
    {
        if (page.Texture == null)
        {
            page.Texture = new RenderTarget2D(
                _graphics,
                (int)page.Size,
                (int)page.Size,
                false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents
            );

            _graphics.SetRenderTarget((RenderTarget2D)page.Texture);
            _graphics.Clear(Color.Transparent);
            _graphics.SetRenderTarget(null);
        }

        _graphics.SetRenderTarget((RenderTarget2D)page.Texture);

        _sb.Begin(
            SpriteSortMode.Deferred,
            BlendState.NonPremultiplied
        );

        _sb.Draw(src, region, Color.White);

        _sb.End();

        _graphics.SetRenderTarget(null);
    }
}
