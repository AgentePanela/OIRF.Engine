using System.Collections.Generic;
using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using SpriteFontBase = FontStashSharp.SpriteFontBase;
using ApiTextStyle = FontStashSharp.TextStyle;
using HAlign = Engine.Client.UI.HorizontalAlignment;

namespace Engine.Client.UI;

public readonly record struct RichTextFont(string? Family, float Size, FontVariant Variant, Color Color);

public sealed class RichTextLayout
{
    internal readonly record struct RunIcon(Texture2D Texture, Rectangle Source, Vector2 Size);
    internal readonly record struct Run(SpriteFontBase Font, SpriteFontBase DisplayFont, string Text, Color Color, ApiTextStyle Style, float Width,
        Color? OutlineColor, float OutlineSize, RunIcon? Icon = null);
    internal readonly record struct Line(List<Run> Runs, float Width, float Height, float OffsetY);
    private readonly record struct Piece(string Text, FormattedStyle Style, float Width, RunIcon? Icon = null);

    internal List<Line> Lines { get; }

    /// <summary>
    /// Width of the widest line and the height of every line together.
    /// </summary>
    public Vector2 Size { get; }

    private RichTextLayout(List<Line> lines, Vector2 size)
    {
        Lines = lines;
        Size = size;
    }

    /// <param name="alpha">Multiplies every color drawn, outlines included.</param>
    public void Draw(ShapeBatch sb, Vector2 origin, float width, HAlign align, float uiScale,
        float clipTop = float.NegativeInfinity, float clipBottom = float.PositiveInfinity, float alpha = 1f)
    {
        var displayScale = new Vector2(1f / MathHelper.Max(uiScale, 0.05f));

        foreach (var line in Lines)
        {
            var lineTop = origin.Y + line.OffsetY;
            if (lineTop + line.Height < clipTop)
                continue;

            if (lineTop > clipBottom)
                break;

            var x = align switch
            {
                HAlign.Center => origin.X + (width - line.Width) / 2f,
                HAlign.Right => origin.X + width - line.Width,
                _ => origin.X, // left, stretch
            };

            foreach (var run in line.Runs)
            {
                if (run.Icon is { } icon)
                {
                    var iconPos = SnapToPixel(new Vector2(x, lineTop + (line.Height - icon.Size.Y) / 2f), uiScale);
                    sb.Draw(icon.Texture, new RectangleF(iconPos.X, iconPos.Y, icon.Size.X, icon.Size.Y),
                        new RectangleF(icon.Source.X, icon.Source.Y, icon.Source.Width, icon.Source.Height), Color.White * alpha);
                    x += run.Width;
                    continue;
                }

                var y = lineTop + (line.Height - LineHeight(run.Font));
                var position = SnapToPixel(new Vector2(x, y), uiScale);
                if (run.OutlineColor is { } outlineColor && run.OutlineSize > 0f)
                {
                    var step = System.MathF.Max(1f, System.MathF.Round(run.OutlineSize * uiScale)) / uiScale;
                    for (var ox = -1; ox <= 1; ox++)
                    {
                        for (var oy = -1; oy <= 1; oy++)
                        {
                            if (ox == 0 && oy == 0)
                                continue;

                            sb.DrawString(run.DisplayFont, run.Text, position + new Vector2(ox * step, oy * step), outlineColor * alpha,
                                scale: displayScale, textStyle: run.Style);
                        }
                    }
                }

                sb.DrawString(run.DisplayFont, run.Text, position, run.Color * alpha, scale: displayScale, textStyle: run.Style);
                x += run.Width;
            }
        }
    }

    /// <inheritdoc cref="Control.SnapToPixel"/>
    private static Vector2 SnapToPixel(Vector2 logical, float uiScale)
    {
        if (uiScale <= 0f)
            return logical;

        return new Vector2(System.MathF.Round(logical.X * uiScale), System.MathF.Round(logical.Y * uiScale)) / uiScale;
    }

    private static SpriteFontBase ResolveFont(IFontManager fonts, RichTextFont baseFont, FormattedStyle style, float scale)
    {
        var variant = baseFont.Variant | style.Variant;
        var family = style.FontFamily ?? baseFont.Family;
        var size = (style.FontSize ?? baseFont.Size) * scale;
        return family is null ? fonts.Get(size, variant) : fonts.Get(size, family, variant);
    }

    private static float LineHeight(SpriteFontBase font) => font.MeasureString("Ag").Y;

    public static RichTextLayout Build(FormattedMessage message, RichTextFont baseFont, float maxWidth, bool wrap, float uiScale)
    {
        var fonts = IoCManager.Resolve<IFontManager>();
        var assets = IoCManager.Resolve<IAssetManager>();
        var displayScale = MathHelper.Max(uiScale, 0.05f);
        var fontCache = new Dictionary<FormattedStyle, (SpriteFontBase Font, float LineHeight)>();
        var displayFontCache = new Dictionary<FormattedStyle, SpriteFontBase>();

        (SpriteFontBase Font, float LineHeight) FontInfo(FormattedStyle style)
        {
            if (!fontCache.TryGetValue(style, out var info))
            {
                var font = ResolveFont(fonts, baseFont, style, 1f);
                fontCache[style] = info = (font, LineHeight(font));
            }

            return info;
        }

        SpriteFontBase Font(FormattedStyle style) => FontInfo(style).Font;

        SpriteFontBase DisplayFont(FormattedStyle style)
        {
            if (!displayFontCache.TryGetValue(style, out var font))
                displayFontCache[style] = font = ResolveFont(fonts, baseFont, style, displayScale);
            return font;
        }

        Color ResolveColor(FormattedStyle style) => style.Color ?? baseFont.Color;

        // merges consecutive same-style pieces into one draw call before starting a new Run
        List<Run> BuildRuns(List<Piece> linePieces)
        {
            var runs = new List<Run>();
            var start = 0;
            for (var i = 1; i <= linePieces.Count; i++)
            {
                if (i < linePieces.Count && linePieces[i].Icon is null && linePieces[start].Icon is null
                    && linePieces[i].Style.Equals(linePieces[start].Style))
                    continue;

                var style = linePieces[start].Style;
                if (linePieces[start].Icon is { } icon)
                {
                    runs.Add(new Run(Font(style), DisplayFont(style), "", Color.White, ApiTextStyle.None, linePieces[start].Width,
                        null, 0f, icon));
                    start = i;
                    continue;
                }

                var width = 0f;
                var parts = new string[i - start];
                for (var j = start; j < i; j++)
                {
                    parts[j - start] = linePieces[j].Text;
                    width += linePieces[j].Width;
                }

                runs.Add(new Run(Font(style), DisplayFont(style), string.Concat(parts), ResolveColor(style), style.Decoration.ToTextStyle(), width,
                    style.OutlineColor, style.OutlineSize));
                start = i;
            }

            return runs;
        }

        var lines = new List<Line>();
        var pieces = new List<Piece>(); // pending pieces for the line currently being built
        var lineWidth = 0f;
        var lineHeight = 0f;
        var totalWidth = 0f;
        var totalHeight = 0f;
        Piece? pendingSpace = null;

        void EndLine()
        {
            var runs = BuildRuns(pieces);
            var height = lineHeight > 0f ? lineHeight : FontInfo(default).LineHeight;
            lines.Add(new Line(runs, lineWidth, height, totalHeight));
            totalWidth = MathHelper.Max(totalWidth, lineWidth);
            totalHeight += height;

            pieces = new List<Piece>();
            lineWidth = 0f;
            lineHeight = 0f;
            pendingSpace = null;
        }

        void AddPiece(Piece piece)
        {
            pieces.Add(piece);
            lineWidth += piece.Width;
        }

        foreach (var token in Tokenize(message))
        {
            if (token.IsNewline)
            {
                EndLine();
                continue;
            }

            if (token.IsWhitespace)
            {
                var (spaceText, spaceStyle, _) = token.WordPieces[0];
                var (font, height) = FontInfo(spaceStyle);
                lineHeight = MathHelper.Max(lineHeight, height);
                pendingSpace = new Piece(spaceText, spaceStyle, font.MeasureString(spaceText).X);
                continue;
            }
            var resolved = new List<Piece>(token.WordPieces.Count);
            var wordWidth = 0f;
            foreach (var (text, style, inlineIcon) in token.WordPieces)
            {
                var (font, height) = FontInfo(style);

                if (inlineIcon is { } wanted)
                {
                    if (!assets.GetTexture(wanted.Key, out var sprite, out var page) || sprite.Region.Height <= 0)
                    {
                        Log.Warn($"Rich text icon '{wanted.Key}' is unknown.");
                        continue;
                    }

                    var iconHeight = wanted.Height ?? height;
                    var iconSize = new Vector2(iconHeight * sprite.Region.Width / sprite.Region.Height, iconHeight);
                    lineHeight = MathHelper.Max(lineHeight, iconHeight);
                    resolved.Add(new Piece("", style, iconSize.X, new RunIcon(page.Texture, sprite.Region, iconSize)));
                    wordWidth += iconSize.X;
                    continue;
                }

                lineHeight = MathHelper.Max(lineHeight, height);
                var width = font.MeasureString(text).X;
                resolved.Add(new Piece(text, style, width));
                wordWidth += width;
            }

            var prefixWidth = pendingSpace?.Width ?? 0f;

            if (wrap && pieces.Count > 0 && lineWidth + prefixWidth + wordWidth > maxWidth)
            {
                EndLine();
                pendingSpace = null;
                prefixWidth = 0f;
            }

            if (pendingSpace is { } space)
            {
                AddPiece(space);
                pendingSpace = null;
            }

            if (wrap && wordWidth > maxWidth && pieces.Count == 0)
            {
                foreach (var piece in resolved)
                {
                    if (piece.Icon is not null)
                    {
                        if (lineWidth + piece.Width > maxWidth && pieces.Count > 0)
                            EndLine();

                        AddPiece(piece);
                        continue;
                    }

                    var font = Font(piece.Style);
                    foreach (var ch in piece.Text)
                    {
                        var chText = ch.ToString();
                        var chWidth = font.MeasureString(chText).X;
                        if (lineWidth + chWidth > maxWidth && pieces.Count > 0)
                            EndLine();

                        AddPiece(new Piece(chText, piece.Style, chWidth));
                    }
                }
            }
            else
            {
                foreach (var piece in resolved)
                    AddPiece(piece);
            }
        }

        EndLine();

        return new RichTextLayout(lines, new Vector2(totalWidth, totalHeight));
    }

    private readonly record struct Token(bool IsNewline, bool IsWhitespace, List<(string Text, FormattedStyle Style, InlineIcon? Icon)> WordPieces);

    private static IEnumerable<Token> Tokenize(FormattedMessage message)
    {
        var wordPieces = new List<(string Text, FormattedStyle Style, InlineIcon? Icon)>();
        foreach (var segment in message.Segments)
        {
            var text = segment.Text;
            var style = segment.Style;
            var i = 0;

            if (segment.Icon is { } icon)
            {
                if (wordPieces.Count > 0)
                {
                    yield return new Token(false, false, wordPieces);
                    wordPieces = new List<(string, FormattedStyle, InlineIcon?)>();
                }

                yield return new Token(false, false, new List<(string, FormattedStyle, InlineIcon?)> { ("", style, icon) });
                continue;
            }

            while (i < text.Length)
            {
                var c = text[i];

                if (c == '\n')
                {
                    if (wordPieces.Count > 0)
                    {
                        yield return new Token(false, false, wordPieces);
                        wordPieces = new List<(string, FormattedStyle, InlineIcon?)>();
                    }

                    yield return new Token(true, false, new List<(string, FormattedStyle, InlineIcon?)>());
                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (wordPieces.Count > 0)
                    {
                        yield return new Token(false, false, wordPieces);
                        wordPieces = new List<(string, FormattedStyle, InlineIcon?)>();
                    }

                    var start = i;
                    while (i < text.Length && text[i] != '\n' && char.IsWhiteSpace(text[i]))
                        i++;

                    yield return new Token(false, true, new List<(string, FormattedStyle, InlineIcon?)> { (text[start..i], style, null) });
                    continue;
                }

                var wordStart = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;

                wordPieces.Add((text[wordStart..i], style, null));
            }
        }

        if (wordPieces.Count > 0)
            yield return new Token(false, false, wordPieces);
    }
}
