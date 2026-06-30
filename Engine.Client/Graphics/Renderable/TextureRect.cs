using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// Represents a texture "fragment" that contains all fiels to be rendered by SpriteBatch.
/// </summary>
public struct TextureRect : IRenderable
{
    public int Layer { get; set; }
    public SamplerState? SamplerState { get; set; }
    public Texture2D Texture { get; }

    /// Local transform
    public Vector2 Offset;
    public Vector2 Origin;
    public float Rotation;
    public Vector2 Scale = Vector2.One;

    /// Rendering
    public Color Color = Color.White;
    public Rectangle Region;
    public float Depth;

    public bool Visible = true;

    public TextureRect(Texture2D texture)
    {
        Texture = texture;
        Region = texture.Bounds;
        Origin = new Vector2(texture.Width / 2f, texture.Height / 2f);
    }

    public TextureRect (
        Texture2D texture,
        Rectangle region,
        Vector2 offset,
        Vector2 origin,
        float rotation,
        Vector2 scale,
        Color color,
        float depth,
        bool visible = true )
    {
        Texture = texture;
        Region = region;
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
        renderer.DrawTexture(this, pos);
    }
}
