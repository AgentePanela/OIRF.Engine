using Engine.Client.Assets;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// Represents a sprite "fragment" that contains all fiels to be rendered by SpriteBatch.
/// </summary>
public struct Sprite2D : IRenderable
{
    /// <summary>
    /// Get a Sprite2D using the sprite key.
    /// </summary>
    public static Sprite2D GetFromAtlas(string key)
    {
        var asset = IoCManager.Resolve<IAssetManager>();
        asset.GetSprite(key, out var sprite);
        return sprite;
    }

    public int Layer { get; set; }
    public SamplerState? SamplerState { get; set; }

    /// <summary>
    ///  Asset key (ex: "Machines/Computer")
    /// </summary>  
    public string Key { get; }
    // Sprite data get from asset manager when creating a new sprite 2d
    public int Width { get; }
    public int Height { get; }

    /// Local transform
    public Vector2 Offset;
    public Vector2 Origin;
    public float Rotation;
    public Vector2 Scale = Vector2.One;

    /// Rendering
    public Color Color = Color.White;
    public float Depth { get; set; }
    public SpriteEffects Effects = SpriteEffects.None;

    public bool Visible = true;

    // Cache
    internal Texture2D CachedTexture { get; set; }
    internal Rectangle CachedRegion { get; set; }

    public Sprite2D(string spriteKey)
    {
        Key = spriteKey;
    }

    internal Sprite2D(string spriteKey, int width, int height)
    {
        Key = spriteKey;
        Width = width;
        Height = height;
        Origin = new Vector2(Width / 2f, Height / 2f);
    }

    public Sprite2D(
        string spriteKey,
        Vector2 offset,
        Vector2 origin,
        float rotation,
        Vector2 scale,
        Color color,
        float depth,
        bool visible = true)
    {
        Key = spriteKey;

        Offset = offset;
        Origin = origin;
        Rotation = rotation;
        Scale = scale;

        Color = color;
        Depth = depth;
        Visible = visible;
    }

    public void Draw(RenderManager renderer, Vector2 pos)
    {
        renderer.DrawSprite(this, pos);
    }
}
