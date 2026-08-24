using System;
using System.Text;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpriteFontBase = FontStashSharp.SpriteFontBase;

namespace Engine.Client.Graphics;

/// <summary>
/// A simple text renderable. Always per-instance - no named/semantic style lookup here,
/// that's a separate concern being redesigned on top of this.
/// </summary>
public struct Label2D : IRenderable
{
    public int Layer { get; set; }
    public SamplerState? SamplerState { get; set; }

    /// <summary>
    /// Font this label draws with.
    /// </summary>
    public SpriteFontBase? Font { get; private set; }

    public string String { get; set; }

    /// Local transform
    public Vector2 Origin;
    public float Rotation;
    public Vector2 Scale = Vector2.One;

    /// Rendering
    public Color Color = Color.White;
    public TextDecoration Decoration;
    public float Depth { get; set; }

    public bool Visible = true;

    public bool ShadowEnabled;
    public Color ShadowColor = Color.Black;
    public Vector2 ShadowOffset = new Vector2(1f, 1f);

    public bool OutlineEnabled;
    public Color OutlineColor = Color.Black;
    public int OutlineThickness = 1;

    public Label2D(string str)
    {
        Font = IoCManager.Resolve<IFontManager>().Get();
        String = str;
    }
    public Label2D(string str, float size)
    {
        Font = IoCManager.Resolve<IFontManager>().Get(size);
        String = str;
    }

    public Label2D(string str, float size, string family, FontVariant variant = FontVariant.Regular)
    {
        Font = IoCManager.Resolve<IFontManager>().Get(size, family, variant);
        String = str;
    }

    public Label2D(SpriteFontBase? font, string str)
    {
        Font = font;
        String = str;
    }

    public Label2D(
        SpriteFontBase? font,
        string str,
        Vector2 origin,
        float rotation,
        Vector2 scale,
        Color color,
        float depth,
        bool visible = true)
        : this(font, str)
    {
        Origin = origin;
        Rotation = rotation;
        Scale = scale;
        Color = color;
        Depth = depth;
        Visible = visible;
    }

    /// <summary>
    /// Updates the font size (recreate the <see cref="SpriteFontBase"/> instance);
    /// </summary>
    /// <param name="size"></param>
    public void SetFontSize(float size)
    {
        Font = IoCManager.Resolve<IFontManager>().Get(size);
    }

    /// <summary>
    /// Switches this label to a specific font family/variant at the given size.
    /// </summary>
    public void SetFont(float size, string family, FontVariant variant = FontVariant.Regular)
    {
        Font = IoCManager.Resolve<IFontManager>().Get(size, family, variant);
    }

    private readonly SpriteFontBase ResolveFont()
        => Font ?? IoCManager.Resolve<IFontManager>().GetFallback();

    /// <summary>
    /// Measures <see cref="String"/> as it would be drawn with <see cref="Font"/>.
    /// </summary>
    public readonly Vector2 Measure()
        => ResolveFont().MeasureString(String ?? string.Empty);

    /// <summary>
    /// Origin that centers this label on its draw position.
    /// </summary>
    public readonly Vector2 GetCenteredOrigin()
        => Measure() / 2f;

    /// <summary>
    /// Wraps <see cref="String"/> onto multiple lines (separated by '\n') so no line exceeds maxWidth.
    /// A single word wider than maxWidth on its own gets split by character as a last resort.
    /// </summary>
    public readonly string WrapText(float maxWidth)
    {
        var text = String ?? string.Empty;
        if (text.Length == 0)
            return string.Empty;

        var font = ResolveFont();
        var words = text.Split(' ', StringSplitOptions.None);
        var builder = new StringBuilder();
        var line = string.Empty;

        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var test = string.IsNullOrEmpty(line) ? word : $"{line} {word}";

            if (font.MeasureString(test).X <= maxWidth)
            {
                line = test;
                continue;
            }

            if (!string.IsNullOrEmpty(line))
            {
                if (builder.Length > 0)
                    builder.Append('\n');

                builder.Append(line);
                line = word;
                continue;
            }

            // word itself is too wide, split by chars
            var charLine = string.Empty;
            foreach (var ch in word)
            {
                var charTest = charLine + ch;
                if (font.MeasureString(charTest).X <= maxWidth || charLine.Length == 0)
                {
                    charLine = charTest;
                }
                else
                {
                    if (builder.Length > 0)
                        builder.Append('\n');

                    builder.Append(charLine);
                    charLine = ch.ToString();
                }
            }

            line = charLine;
        }

        if (!string.IsNullOrEmpty(line))
        {
            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(line);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Truncates <see cref="String"/> with an ellipsis so it fits within maxWidth.
    /// </summary>
    public readonly string TruncateText(float maxWidth, string ellipsis = "...")
    {
        var text = String ?? string.Empty;
        ellipsis ??= "...";

        var font = ResolveFont();
        if (font.MeasureString(text).X <= maxWidth)
            return text;

        if (font.MeasureString(ellipsis).X > maxWidth)
            return string.Empty;

        for (var i = text.Length; i >= 0; i--)
        {
            var candidate = text[..i] + ellipsis;
            if (font.MeasureString(candidate).X <= maxWidth)
                return candidate;
        }

        return string.Empty;
    }

    public void Draw(RenderManager renderer, Vector2 pos)
    {
        renderer.DrawString(this, pos);
    }
}
