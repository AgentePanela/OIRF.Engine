using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// The style in effect at some point in a parsed message.
/// </summary>
public readonly record struct FormattedStyle(
    FontVariant Variant = FontVariant.Regular,
    TextDecoration Decoration = TextDecoration.None,
    Color? Color = null,
    string? FontFamily = null,
    float? FontSize = null);

/// <summary>
/// A run of text sharing one <see cref="FormattedStyle"/>.
/// </summary>
public readonly record struct FormattedSegment(string Text, FormattedStyle Style);

/// <summary>
/// A rich text string parsed into styled segments
/// </summary>
public sealed class FormattedMessage
{
    public IReadOnlyList<FormattedSegment> Segments { get; }

    private FormattedMessage(List<FormattedSegment> segments) => Segments = segments;

    static FormattedMessage()
    {
        MarkupTagRegistry.Register(new BoldTag());
        MarkupTagRegistry.Register(new ItalicTag());
        MarkupTagRegistry.Register(new UnderlineTag());
        MarkupTagRegistry.Register(new StrikethroughTag());
        MarkupTagRegistry.Register(new ColorTag());
        MarkupTagRegistry.Register(new FontTag());
        MarkupTagRegistry.Register(new SizeTag());
    }

    /// <summary>
    /// Parses [tag]/[tag=value]/[/tag] markup into segments.
    /// </summary>
    public static FormattedMessage Parse(string source)
    {
        var segments = new List<FormattedSegment>();
        var styleStack = new Stack<(string TagName, FormattedStyle Style)>();
        var currentStyle = default(FormattedStyle);
        var text = new StringBuilder();

        void Flush()
        {
            if (text.Length == 0)
                return;

            segments.Add(new FormattedSegment(text.ToString(), currentStyle));
            text.Clear();
        }

        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];

            if (c == '[' && i + 1 < source.Length && source[i + 1] == '[')
            {
                text.Append('[');
                i += 2;
                continue;
            }

            if (c == ']' && i + 1 < source.Length && source[i + 1] == ']')
            {
                text.Append(']');
                i += 2;
                continue;
            }

            if (c != '[')
            {
                text.Append(c);
                i++;
                continue;
            }

            var close = source.IndexOf(']', i + 1);
            if (close < 0)
            {
                text.Append(source, i, source.Length - i);
                break;
            }

            var inner = source.Substring(i + 1, close - i - 1);
            var isClosing = inner.StartsWith('/');
            var body = isClosing ? inner[1..] : inner;
            var eq = body.IndexOf('=');
            var tagName = eq >= 0 ? body[..eq] : body;
            var tagValue = eq >= 0 ? body[(eq + 1)..] : null;

            if (!MarkupTagRegistry.TryGet(tagName, out var handler))
            {
                text.Append(source, i, close - i + 1);
                i = close + 1;
                continue;
            }

            if (isClosing)
            {
                if (styleStack.Count == 0 || !string.Equals(styleStack.Peek().TagName, tagName, StringComparison.OrdinalIgnoreCase))
                {
                    text.Append(source, i, close - i + 1);
                    i = close + 1;
                    continue;
                }

                Flush();
                currentStyle = styleStack.Pop().Style;
            }
            else
            {
                Flush();
                styleStack.Push((tagName, currentStyle));
                currentStyle = handler.Apply(currentStyle, tagValue);
            }

            i = close + 1;
        }

        Flush();
        return new FormattedMessage(segments);
    }

    private sealed class BoldTag : IMarkupTag
    {
        public string Name => "b";
        public FormattedStyle Apply(FormattedStyle current, string? value)
            => current with { Variant = current.Variant | FontVariant.Bold };
    }

    private sealed class ItalicTag : IMarkupTag
    {
        public string Name => "i";
        public FormattedStyle Apply(FormattedStyle current, string? value)
            => current with { Variant = current.Variant | FontVariant.Italic };
    }

    private sealed class UnderlineTag : IMarkupTag
    {
        public string Name => "u";
        public FormattedStyle Apply(FormattedStyle current, string? value)
            => current with { Decoration = current.Decoration | TextDecoration.Underline };
    }

    private sealed class StrikethroughTag : IMarkupTag
    {
        public string Name => "s";
        public FormattedStyle Apply(FormattedStyle current, string? value)
            => current with { Decoration = current.Decoration | TextDecoration.Strikethrough };
    }

    private sealed class ColorTag : IMarkupTag
    {
        public string Name => "color";
        public FormattedStyle Apply(FormattedStyle current, string? value)
            => value is null ? current : current with { Color = ParseColor(value) ?? current.Color };
    }

    private sealed class FontTag : IMarkupTag
    {
        public string Name => "font";
        public FormattedStyle Apply(FormattedStyle current, string? value)
            => current with { FontFamily = value };
    }

    private sealed class SizeTag : IMarkupTag
    {
        public string Name => "size";
        public FormattedStyle Apply(FormattedStyle current, string? value)
            => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
                ? current with { FontSize = size }
                : current;
    }

    private static Color? ParseColor(string str)
    {
        str = str.Trim();

        if (str.StartsWith('#'))
        {
            var hex = str[1..];
            return hex.Length switch
            {
                3 => new Color(HexToByte(hex[0], hex[0]), HexToByte(hex[1], hex[1]), HexToByte(hex[2], hex[2])),
                6 => new Color(HexToByte(hex[0], hex[1]), HexToByte(hex[2], hex[3]), HexToByte(hex[4], hex[5])),
                8 => new Color(HexToByte(hex[0], hex[1]), HexToByte(hex[2], hex[3]), HexToByte(hex[4], hex[5]), HexToByte(hex[6], hex[7])),
                _ => null,
            };
        }

        var prop = typeof(Color).GetProperty(str, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        return prop?.GetValue(null) as Color?;
    }

    private static byte HexToByte(char hi, char lo)
        => byte.TryParse(stackalloc[] { hi, lo }, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) ? b : (byte)0;
}
