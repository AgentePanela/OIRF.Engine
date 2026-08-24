using System.Collections.Generic;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using SpriteFontBase = FontStashSharp.SpriteFontBase;
using ApiTextStyle = FontStashSharp.TextStyle;

namespace Engine.Client.UI;

public sealed partial class RichLabel
{
    private readonly record struct Run(SpriteFontBase Font, string Text, Color Color, ApiTextStyle Style, float Width);
    private readonly record struct Line(List<Run> Runs, float Width, float Height, float OffsetY);
    private readonly record struct LayoutResult(List<Line> Lines, Vector2 Size);
    private readonly record struct Piece(string Text, FormattedStyle Style, float Width);

    // Measure and Draw can legitimately be called with different maxWidth - a Stretch-aligned
    // control (the default) ignores an explicit Width during Arrange, so Bounds.Width can end up
    // wider than what Width clamped MeasureCore's availableSize to. A single shared cache slot
    // would thrash forever between the two, rebuilding the whole layout every single frame - two
    // slots means each caller just keeps hitting its own.
    private struct LayoutCache
    {
        public FormattedMessage? Message;
        public bool Wrap;
        public float Width;
        public string? FontFamily;
        public float FontSize;
        public FontVariant FontVariant;
        public LayoutResult Result;
        public bool Valid;

        public readonly bool Matches(RichLabel label, bool wrap, float width)
            => Valid
                && ReferenceEquals(Message, label._message)
                && Wrap == wrap
                && (!wrap || Width == width)
                && ReferenceEquals(FontFamily, label.FontFamily)
                && FontSize == label.FontSize
                && FontVariant == label.FontVariant;
    }

    private LayoutCache _measureCache;
    private LayoutCache _drawCache;

    private LayoutResult EnsureLayout(float maxWidth, bool wrap, bool forDraw)
    {
        ref var cache = ref forDraw ? ref _drawCache : ref _measureCache;
        if (cache.Matches(this, wrap, maxWidth))
            return cache.Result;

        var result = BuildLayout(maxWidth, wrap);

        cache = new LayoutCache
        {
            Message = _message,
            Wrap = wrap,
            Width = maxWidth,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontVariant = FontVariant,
            Result = result,
            Valid = true,
        };
        return result;
    }

    private SpriteFontBase ResolveFont(IFontManager fonts, FormattedStyle style)
    {
        var variant = FontVariant | style.Variant;
        var family = style.FontFamily ?? FontFamily;
        var size = style.FontSize ?? FontSize;
        return family is null ? fonts.Get(size, variant) : fonts.Get(size, family, variant);
    }

    private Color ResolveColor(FormattedStyle style) => style.Color ?? Color;

    private static float LineHeight(SpriteFontBase font) => font.MeasureString("Ag").Y;

    private LayoutResult BuildLayout(float maxWidth, bool wrap)
    {
        var fonts = IoCManager.Resolve<IFontManager>();
        var fontCache = new Dictionary<FormattedStyle, SpriteFontBase>();

        SpriteFontBase Font(FormattedStyle style)
        {
            if (!fontCache.TryGetValue(style, out var font))
                fontCache[style] = font = ResolveFont(fonts, style);
            return font;
        }

        // merges consecutive same-style pieces into one draw call before starting a new Run
        List<Run> BuildRuns(List<Piece> linePieces)
        {
            var runs = new List<Run>();
            var start = 0;
            for (var i = 1; i <= linePieces.Count; i++)
            {
                if (i < linePieces.Count && linePieces[i].Style.Equals(linePieces[start].Style))
                    continue;

                var style = linePieces[start].Style;
                var width = 0f;
                var parts = new string[i - start];
                for (var j = start; j < i; j++)
                {
                    parts[j - start] = linePieces[j].Text;
                    width += linePieces[j].Width;
                }

                runs.Add(new Run(Font(style), string.Concat(parts), ResolveColor(style), style.Decoration.ToTextStyle(), width));
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
            var height = lineHeight > 0f ? lineHeight : LineHeight(Font(default));
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

        foreach (var token in Tokenize(_message))
        {
            if (token.IsNewline)
            {
                EndLine();
                continue;
            }

            if (token.IsWhitespace)
            {
                var (spaceText, spaceStyle) = token.WordPieces[0];
                var font = Font(spaceStyle);
                lineHeight = MathHelper.Max(lineHeight, LineHeight(font));
                pendingSpace = new Piece(spaceText, spaceStyle, font.MeasureString(spaceText).X);
                continue;
            }
            var resolved = new List<Piece>(token.WordPieces.Count);
            var wordWidth = 0f;
            foreach (var (text, style) in token.WordPieces)
            {
                var font = Font(style);
                lineHeight = MathHelper.Max(lineHeight, LineHeight(font));
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

        return new LayoutResult(lines, new Vector2(totalWidth, totalHeight));
    }

    private readonly record struct Token(bool IsNewline, bool IsWhitespace, List<(string Text, FormattedStyle Style)> WordPieces);

    private static IEnumerable<Token> Tokenize(FormattedMessage message)
    {
        var wordPieces = new List<(string Text, FormattedStyle Style)>();
        foreach (var segment in message.Segments)
        {
            var text = segment.Text;
            var style = segment.Style;
            var i = 0;

            while (i < text.Length)
            {
                var c = text[i];

                if (c == '\n')
                {
                    if (wordPieces.Count > 0)
                    {
                        yield return new Token(false, false, wordPieces);
                        wordPieces = new List<(string, FormattedStyle)>();
                    }

                    yield return new Token(true, false, new List<(string, FormattedStyle)>());
                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (wordPieces.Count > 0)
                    {
                        yield return new Token(false, false, wordPieces);
                        wordPieces = new List<(string, FormattedStyle)>();
                    }

                    var start = i;
                    while (i < text.Length && text[i] != '\n' && char.IsWhiteSpace(text[i]))
                        i++;

                    yield return new Token(false, true, new List<(string, FormattedStyle)> { (text[start..i], style) });
                    continue;
                }

                var wordStart = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;

                wordPieces.Add((text[wordStart..i], style));
            }
        }

        if (wordPieces.Count > 0)
            yield return new Token(false, false, wordPieces);
    }
}
