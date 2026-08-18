using System;
using System.Collections.Generic;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.Common;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using SpriteFontBase = FontStashSharp.SpriteFontBase;

namespace Engine.Client.UI;

public sealed partial class TextEdit
{
    // Text.Split allocates a new array every call
    private string[]? _linesCache;
    private string? _linesCacheText;

    /// <summary>
    /// Text split into "\n"-delimited (logical) lines.
    /// </summary>
    private string[] Lines
    {
        get
        {
            if (_linesCache is null || !ReferenceEquals(_linesCacheText, Text))
            {
                _linesCache = Text.Split('\n');
                _linesCacheText = Text;
            }

            return _linesCache;
        }
    }

    private readonly record struct VisualLine(int LogicalLine, int Start, int End);

    private List<VisualLine>? _visualLinesCache;
    private string? _visualLinesCacheText;
    private float _visualLinesCacheWidth;
    private bool _visualLinesCacheAutoWrap;
    private string? _visualLinesCacheFontFamily;
    private float _visualLinesCacheFontSize;
    private FontVariant _visualLinesCacheFontVariant;

    // Visual rows for the current Text/Bounds/AutoWrap state.
    private List<VisualLine> GetVisualLines()
    {
        var lines = Lines;
        var autoWrap = AutoWrap;
        var maxWidth = autoWrap
            ? MathHelper.Max(1f, PanelRect(Bounds).Width - Padding.Left - Padding.Right - _scrollBarReserve)
            : 0f;

        if (_visualLinesCache is not null
            && ReferenceEquals(_visualLinesCacheText, Text)
            && _visualLinesCacheAutoWrap == autoWrap
            && (!autoWrap || _visualLinesCacheWidth == maxWidth)
            && ReferenceEquals(_visualLinesCacheFontFamily, FontFamily)
            && _visualLinesCacheFontSize == FontSize
            && _visualLinesCacheFontVariant == FontVariant)
        {
            return _visualLinesCache;
        }

        var result = new List<VisualLine>(lines.Length);

        if (!autoWrap)
        {
            for (var i = 0; i < lines.Length; i++)
                result.Add(new VisualLine(i, 0, lines[i].Length));
        }
        else
        {
            var font = ResolveFont(IoCManager.Resolve<IFontManager>());

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (start, end) in WrapLine(lines[i], maxWidth, font))
                    result.Add(new VisualLine(i, start, end));
            }
        }

        _visualLinesCache = result;
        _visualLinesCacheText = Text;
        _visualLinesCacheWidth = maxWidth;
        _visualLinesCacheAutoWrap = autoWrap;
        _visualLinesCacheFontFamily = FontFamily;
        _visualLinesCacheFontSize = FontSize;
        _visualLinesCacheFontVariant = FontVariant;
        return result;
    }

    private static IEnumerable<(int start, int end)> WrapLine(string line, float maxWidth, SpriteFontBase font)
    {
        if (line.Length == 0)
        {
            yield return (0, 0);
            yield break;
        }

        var segStart = 0;
        var lastSpace = -1;
        var width = 0f;
        for (var i = 0; i < line.Length; i++)
        {
            var charWidth = font.MeasureString(line[i..(i + 1)]).X;

            if (width + charWidth <= maxWidth)
            {
                width += charWidth;
                if (line[i] == ' ')
                    lastSpace = i;

                continue;
            }

            if (lastSpace > segStart)
            {
                yield return (segStart, lastSpace);
                segStart = lastSpace + 1; // skip the space itself
            }
            else if (i > segStart)
            {
                yield return (segStart, i);
                segStart = i;
            }
            else
            {
                yield return (segStart, i + 1);
                segStart = i + 1;
            }

            lastSpace = -1;
            width = 0f;
            i = segStart - 1; // re-measure from the new segment start
        }

        yield return (segStart, line.Length);
    }

    /// <summary>
    /// Finds which VisualLine a (logicalLine, column) caret position renders on
    /// </summary>
    private static int FindVisualLineIndex(List<VisualLine> visualLines, int logicalLine, int column)
    {
        var lastMatch = 0;

        for (var i = 0; i < visualLines.Count; i++)
        {
            if (visualLines[i].LogicalLine != logicalLine)
                continue;

            lastMatch = i;
            if (column <= visualLines[i].End)
                return i;
        }

        return lastMatch;
    }

    private void MoveCaretVertical(int lineDelta, bool extendSelection)
    {
        var font = ResolveFont(IoCManager.Resolve<IFontManager>());
        var lines = Lines;
        var visualLines = GetVisualLines();
        var (logLine, column) = IndexToLineColumn(_caret);
        var visualIndex = FindVisualLineIndex(visualLines, logLine, column);
        var v = visualLines[visualIndex];

        var preferredX = _caretPreferredX ?? font.MeasureString(lines[logLine][v.Start..column]).X;
        var newVisualIndex = Math.Clamp(visualIndex + lineDelta, 0, visualLines.Count - 1);
        var newV = visualLines[newVisualIndex];
        var newColumn = newV.Start + HitTestColumn(lines[newV.LogicalLine][newV.Start..newV.End], preferredX, font);

        _caret = Math.Clamp(LineColumnToIndex(newV.LogicalLine, newColumn), 0, Text.Length);
        if (!extendSelection)
            _selectionAnchor = _caret;

        _caretPreferredX = preferredX;
        _coalescingTyping = false;
        _coalescingDeleting = false;
    }

    /// <summary>
    /// Converts a flat caret index into (line, column) against the current lines.
    /// </summary>
    private (int line, int column) IndexToLineColumn(int index)
    {
        var lines = Lines;
        var remaining = index;

        for (var i = 0; i < lines.Length; i++)
        {
            if (remaining <= lines[i].Length)
                return (i, remaining);

            remaining -= lines[i].Length + 1; // +1 for the '\n'
        }

        return (lines.Length - 1, lines[^1].Length);
    }

    private int LineColumnToIndex(int line, int column)
    {
        var lines = Lines;
        line = Math.Clamp(line, 0, lines.Length - 1);
        column = Math.Clamp(column, 0, lines[line].Length);

        var index = 0;
        for (var i = 0; i < line; i++)
            index += lines[i].Length + 1;

        return index + column;
    }

    protected override int HitTestIndex(Vector2 screenPos)
    {
        var font = ResolveFont(IoCManager.Resolve<IFontManager>());
        var lines = Lines;
        var visualLines = GetVisualLines();
        var lineHeight = LineHeight(font);
        var rect = PanelRect(Bounds);

        var localY = screenPos.Y - (rect.Y + Padding.Top) + _scrollPixelsY;
        var visualIndex = Math.Clamp((int)(localY / lineHeight), 0, visualLines.Count - 1);
        var v = visualLines[visualIndex];

        var localX = screenPos.X - (rect.X + Padding.Left) + _scrollPixels;
        var localColumn = HitTestColumn(lines[v.LogicalLine][v.Start..v.End], localX, font);

        return LineColumnToIndex(v.LogicalLine, v.Start + localColumn);
    }

    protected override (int start, int end) GetWordBounds(int index)
    {
        // scoped to the clicked LOGICAL line, so a double-click never crosses a "\n"
        var (line, column) = IndexToLineColumn(index);
        var (wordStart, wordEnd) = FindWordBounds(Lines[line], column);
        return (LineColumnToIndex(line, wordStart), LineColumnToIndex(line, wordEnd));
    }

    protected override (int start, int end) GetTripleClickBounds(int index)
    {
        var (line, _) = IndexToLineColumn(index);
        return (LineColumnToIndex(line, 0), LineColumnToIndex(line, Lines[line].Length));
    }

    protected override void ClampScroll()
    {
        var font = ResolveFont(IoCManager.Resolve<IFontManager>());
        var lines = Lines;
        var visualLines = GetVisualLines();
        var (line, column) = IndexToLineColumn(_caret);
        var visualIndex = FindVisualLineIndex(visualLines, line, column);
        var v = visualLines[visualIndex];
        var rect = PanelRect(Bounds);
        var caretX = font.MeasureString(lines[line][v.Start..column]).X;
        var visibleWidth = MathHelper.Max(0, rect.Width - Padding.Left - Padding.Right - _scrollBarReserve);

        if (caretX - _scrollPixels < 0)
            _scrollPixels = caretX;
        else if (caretX - _scrollPixels > visibleWidth)
            _scrollPixels = caretX - visibleWidth;

        _scrollPixels = MathHelper.Max(0, _scrollPixels);

        var lineHeight = LineHeight(font);
        var caretY = visualIndex * lineHeight;
        var visibleHeight = MathHelper.Max(0, rect.Height - Padding.Top - Padding.Bottom);

        if (caretY - _scrollPixelsY < 0)
            _scrollPixelsY = caretY;
        else if (caretY + lineHeight - _scrollPixelsY > visibleHeight)
            _scrollPixelsY = caretY + lineHeight - visibleHeight;

        _scrollPixelsY = MathHelper.Max(0, _scrollPixelsY);
        _vScrollBar.Value = _scrollPixelsY;
    }

    protected internal override bool MouseWheel(int delta)
    {
        var font = ResolveFont(IoCManager.Resolve<IFontManager>());
        var lineHeight = LineHeight(font);
        var totalHeight = GetVisualLines().Count * lineHeight;
        var visibleHeight = MathHelper.Max(0, PanelRect(Bounds).Height - Padding.Top - Padding.Bottom);
        var maxScroll = MathHelper.Max(0, totalHeight - visibleHeight);

        var before = _scrollPixelsY;
        _scrollPixelsY = MathHelper.Clamp(_scrollPixelsY - delta / 120f * lineHeight * 3, 0, maxScroll);
        _vScrollBar.Value = _scrollPixelsY;
        return _scrollPixelsY != before;
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        base.DrawSelf(sb, fontManager, dt);

        _caretBlink += dt;
        if (_caretBlink >= CaretBlinkInterval * 2)
            _caretBlink = 0f;

        var font = ResolveFont(fontManager);
        var lines = Lines;
        var visualLines = GetVisualLines();
        var lineHeight = LineHeight(font);

        var rect = PanelRect(Bounds);
        var textOriginX = rect.X + Padding.Left - _scrollPixels;
        var textOriginY = rect.Y + Padding.Top - _scrollPixelsY;

        var device = GameClient.GraphicsDevice;
        var previousScissor = device.ScissorRectangle;
        var textRect = new Rectangle(rect.X, rect.Y, (int)MathHelper.Max(0, rect.Width - _scrollBarReserve), rect.Height);
        var clipped = Rectangle.Intersect(previousScissor, textRect);
        if (clipped.Width <= 0 || clipped.Height <= 0)
            return;

        sb.End(); // scoped clip so overflowing/scrolled text can't bleed past our own Bounds
        device.ScissorRectangle = clipped;
        sb.Begin(rasterizerState: ScissorRasterizer);
        if (_caret != _selectionAnchor)
        {
            var selStart = Math.Min(_caret, _selectionAnchor);
            var selEnd = Math.Max(_caret, _selectionAnchor);
            var (selStartLine, selStartCol) = IndexToLineColumn(selStart);
            var (selEndLine, selEndCol) = IndexToLineColumn(selEnd);

            for (var vi = 0; vi < visualLines.Count; vi++)
            {
                var v = visualLines[vi];
                if (v.LogicalLine < selStartLine || v.LogicalLine > selEndLine)
                    continue;

                // clip the logical selection range down to this visual row's [Start, End) window
                var segSelStart = v.LogicalLine == selStartLine ? Math.Max(selStartCol, v.Start) : v.Start;
                var segSelEnd = v.LogicalLine == selEndLine ? Math.Min(selEndCol, v.End) : v.End;
                if (segSelStart > segSelEnd)
                    continue; // selection doesn't actually touch this row

                var segmentText = lines[v.LogicalLine][v.Start..v.End];
                var colStart = segSelStart - v.Start;
                var colEnd = segSelEnd - v.Start;

                var selX = textOriginX + font.MeasureString(segmentText[..colStart]).X;

                // only the LAST visual row of a fully-selected logical line also "contains" its
                // trailing newline - give just that one a minimum width so it still shows something.
                var isLastRowOfLine = vi + 1 >= visualLines.Count || visualLines[vi + 1].LogicalLine != v.LogicalLine;
                var selWidth = v.LogicalLine < selEndLine && isLastRowOfLine
                    ? MathHelper.Max(font.MeasureString(segmentText[colStart..]).X, 6f)
                    : font.MeasureString(segmentText[colStart..colEnd]).X;

                var selRect = new RectangleF(selX, textOriginY + vi * lineHeight, selWidth, lineHeight);
                sb.FillRectangle(new Vector2(selRect.X, selRect.Y), new Vector2(selRect.Width, selRect.Height),
                    new ColorGradient(SelectionColor).Resolve(selRect));
            }
        }

        if (Text.Length > 0)
        {
            for (var vi = 0; vi < visualLines.Count; vi++)
            {
                var y = textOriginY + vi * lineHeight;
                if (y + lineHeight < rect.Y || y > rect.Bottom)
                    continue; // fully outside the visible band - scissor would clip it anyway

                var v = visualLines[vi];
                sb.DrawString(font, lines[v.LogicalLine][v.Start..v.End], new Vector2(textOriginX, y), Color);
            }
        }
        else if (!string.IsNullOrEmpty(PlaceholderText))
        {
            sb.DrawString(font, PlaceholderText, new Vector2(textOriginX, textOriginY), PlaceholderColor);
        }

        if (IsFocused && _caretBlink < CaretBlinkInterval)
        {
            var (line, column) = IndexToLineColumn(_caret);
            var visualIndex = FindVisualLineIndex(visualLines, line, column);
            var v = visualLines[visualIndex];

            var caretX = textOriginX + font.MeasureString(lines[line][v.Start..column]).X;
            var caretLineY = textOriginY + visualIndex * lineHeight;
            var caretHeight = CaretHeight > 0 ? CaretHeight : font.MeasureString("Ag").Y;
            var caretY = caretLineY + (lineHeight - caretHeight) / 2f;
            var caretRect = new RectangleF(caretX + CaretOffsetX, caretY + CaretOffsetY, CaretWidth, caretHeight);
            sb.FillRectangle(new Vector2(caretRect.X, caretRect.Y), new Vector2(caretRect.Width, caretRect.Height),
                new ColorGradient(CaretColor).Resolve(caretRect));
        }

        sb.End();
        device.ScissorRectangle = previousScissor;
        sb.Begin(rasterizerState: ScissorRasterizer);
    }
}
