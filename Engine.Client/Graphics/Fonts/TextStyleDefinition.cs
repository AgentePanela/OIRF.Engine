using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Describes a high-level text style.
/// </summary>
public sealed class TextStyleDefinition
{
    public FontKey FontKey { get; set; } = FontKey.Default;
    public Color Color { get; set; } = Color.White;
    public float Scale { get; set; } = 1f;

    public bool ShadowEnabled { get; set; } = false;
    public Color ShadowColor { get; set; } = Color.Black;
    public Vector2 ShadowOffset { get; set; } = new Vector2(1f, 1f);

    public bool OutlineEnabled { get; set; } = false;
    public Color OutlineColor { get; set; } = Color.Black;
    public int OutlineThickness { get; set; } = 1;

    public TextStyleDefinition()
    {
    }

    public TextStyleDefinition(FontKey fontKey, Color color, float scale = 1f)
    {
        FontKey = fontKey;
        Color = color;
        Scale = scale;
    }
}
