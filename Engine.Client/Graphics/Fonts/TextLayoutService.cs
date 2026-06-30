using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using System;
using System.Text;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Text utility helpers: measure, wrap, truncate, centered origin.
/// Keeps layout logic out of RenderManager.
/// </summary>
public sealed class TextLayoutService
{
    private IFontManager Fonts => IoCManager.Resolve<IFontManager>();

    public Vector2 Measure(Label2D label)
    {
        var text = label.String ?? string.Empty;

        if (label.Font is not null)
            return label.Font.MeasureString(text);

        if (label.Style != TextStyle.None)
            return Fonts.Measure(label.Style, text);

        if (label.FontKey != FontKey.None)
            return Fonts.Measure(label.FontKey, text);

        return Fonts.GetFallback().MeasureString(text);
    }

    public Vector2 Measure(string text, FontKey key)
        => Fonts.Measure(key, text);

    public Vector2 Measure(string text, TextStyle style)
        => Fonts.Measure(style, text);

    public Vector2 GetCenteredOrigin(Label2D label)
        => Measure(label) / 2f;

    public string WrapText(string text, float maxWidth, FontKey key)
        => WrapTextInternal(text, maxWidth, Fonts.Get(key));

    public string WrapText(string text, float maxWidth, TextStyle style)
        => WrapTextInternal(text, maxWidth, Fonts.GetForStyle(style));

    public string TruncateText(string text, float maxWidth, FontKey key, string ellipsis = "...")
        => TruncateTextInternal(text, maxWidth, Fonts.Get(key), ellipsis);

    public string TruncateText(string text, float maxWidth, TextStyle style, string ellipsis = "...")
        => TruncateTextInternal(text, maxWidth, Fonts.GetForStyle(style), ellipsis);

    private static string WrapTextInternal(string text, float maxWidth, Microsoft.Xna.Framework.Graphics.SpriteFont font)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

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

    private static string TruncateTextInternal(string text, float maxWidth, Microsoft.Xna.Framework.Graphics.SpriteFont font, string ellipsis)
    {
        text ??= string.Empty;
        ellipsis ??= "...";

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
}
