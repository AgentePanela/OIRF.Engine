using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpriteFontBase = FontStashSharp.SpriteFontBase;

namespace Engine.Client.Graphics;

/// <summary>
/// A simple text renderable.
/// Backward-compatible with raw SpriteFont usage while also supporting FontKey/TextStyle.
/// </summary>
public struct Label2D : IRenderable
{
    public int Layer { get; set; }
    public SamplerState? SamplerState { get; set; }

    /// <summary>
    /// Legacy/raw font path. Still fully supported.
    /// </summary>
    public SpriteFontBase? Font { get; set; }

    /// <summary>
    /// Managed font lookup path.
    /// </summary>
    public FontKey FontKey { get; set; }

    /// <summary>
    /// High-level style lookup path.
    /// </summary>
    public TextStyle Style { get; set; }

    public string String { get; set; }

    /// Local transform
    public Vector2 Origin;
    public float Rotation;
    public Vector2 Scale = Vector2.One;

    /// Rendering
    public Color Color = Color.White;
    public float Depth { get; set; }

    public bool Visible = true;

    /// <summary>
    /// Phase 2 style opt-ins.
    /// These default to false for legacy constructors so old code does not change visually.
    /// </summary>
    public bool UseStyleColor;
    public bool UseStyleScale;
    public bool UseStyleEffects;

    /// <summary>
    /// Per-label text effects. If UseStyleEffects is true and a style is assigned,
    /// style effects win unless you change the label flags manually.
    /// </summary>
    public bool ShadowEnabled;
    public Color ShadowColor;
    public Vector2 ShadowOffset;

    public bool OutlineEnabled;
    public Color OutlineColor;
    public int OutlineThickness;

    /// <summary>
    /// Backward-compatible raw font constructor.
    /// </summary>
    public Label2D(SpriteFontBase font, string str)
    {
        Font = font;
        FontKey = FontKey.None;
        Style = TextStyle.None;
        String = str;

        Origin = Vector2.Zero;
        Rotation = 0f;
        Scale = Vector2.One;

        Color = Color.White;
        Depth = 0f;
        Visible = true;

        UseStyleColor = false;
        UseStyleScale = false;
        UseStyleEffects = false;

        ShadowEnabled = false;
        ShadowColor = Color.Black;
        ShadowOffset = new Vector2(1f, 1f);

        OutlineEnabled = false;
        OutlineColor = Color.Black;
        OutlineThickness = 1;
    }

    /// <summary>
    /// Managed font-key constructor.
    /// </summary>
    public Label2D(FontKey fontKey, string str)
    {
        Font = null;
        FontKey = fontKey;
        Style = TextStyle.None;
        String = str;

        Origin = Vector2.Zero;
        Rotation = 0f;
        Scale = Vector2.One;

        Color = Color.White;
        Depth = 0f;
        Visible = true;

        UseStyleColor = false;
        UseStyleScale = false;
        UseStyleEffects = false;

        ShadowEnabled = false;
        ShadowColor = Color.Black;
        ShadowOffset = new Vector2(1f, 1f);

        OutlineEnabled = false;
        OutlineColor = Color.Black;
        OutlineThickness = 1;
    }

    /// <summary>
    /// Managed style constructor.
    /// </summary>
    public Label2D(TextStyle style, string str)
    {
        Font = null;
        FontKey = FontKey.None;
        Style = style;
        String = str;

        Origin = Vector2.Zero;
        Rotation = 0f;
        Scale = Vector2.One;

        Color = Color.White;
        Depth = 0f;
        Visible = true;

        UseStyleColor = true;
        UseStyleScale = true;
        UseStyleEffects = true;

        ShadowEnabled = false;
        ShadowColor = Color.Black;
        ShadowOffset = new Vector2(1f, 1f);

        OutlineEnabled = false;
        OutlineColor = Color.Black;
        OutlineThickness = 1;
    }

    public Label2D(
        SpriteFontBase font,
        string str,
        Vector2 origin,
        float rotation,
        Vector2 scale,
        Color color,
        float depth,
        bool visible = true)
    {
        Font = font;
        FontKey = FontKey.None;
        Style = TextStyle.None;
        String = str;

        Origin = origin;
        Rotation = rotation;
        Scale = scale;

        Color = color;
        Depth = depth;
        Visible = visible;

        UseStyleColor = false;
        UseStyleScale = false;
        UseStyleEffects = false;

        ShadowEnabled = false;
        ShadowColor = Color.Black;
        ShadowOffset = new Vector2(1f, 1f);

        OutlineEnabled = false;
        OutlineColor = Color.Black;
        OutlineThickness = 1;
    }

    public Label2D(
        FontKey fontKey,
        string str,
        Vector2 origin,
        float rotation,
        Vector2 scale,
        Color color,
        float depth,
        bool visible = true)
    {
        Font = null;
        FontKey = fontKey;
        Style = TextStyle.None;
        String = str;

        Origin = origin;
        Rotation = rotation;
        Scale = scale;

        Color = color;
        Depth = depth;
        Visible = visible;

        UseStyleColor = false;
        UseStyleScale = false;
        UseStyleEffects = false;

        ShadowEnabled = false;
        ShadowColor = Color.Black;
        ShadowOffset = new Vector2(1f, 1f);

        OutlineEnabled = false;
        OutlineColor = Color.Black;
        OutlineThickness = 1;
    }

    public Label2D(
        TextStyle style,
        string str,
        Vector2 origin,
        float rotation,
        Vector2 scale,
        Color color,
        float depth,
        bool visible = true)
    {
        Font = null;
        FontKey = FontKey.None;
        Style = style;
        String = str;

        Origin = origin;
        Rotation = rotation;
        Scale = scale;

        Color = color;
        Depth = depth;
        Visible = visible;

        UseStyleColor = true;
        UseStyleScale = true;
        UseStyleEffects = true;

        ShadowEnabled = false;
        ShadowColor = Color.Black;
        ShadowOffset = new Vector2(1f, 1f);

        OutlineEnabled = false;
        OutlineColor = Color.Black;
        OutlineThickness = 1;
    }

    public void Draw(RenderManager renderer, Vector2 pos)
    {
        renderer.DrawString(this, pos);
    }
}
