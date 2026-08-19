using System;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Shared.Common;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Single-line text input.
/// </summary>
public sealed partial class LineEdit : BaseTextInput
{
    /// <summary>
    /// Fired when Enter is pressed while focused.
    /// </summary>
    public event Action<string>? OnTextEntered;

    public LineEdit()
    {
        StyleAliasses.Add("lineEdit");
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var fonts = IoCManager.Resolve<IFontManager>();
        var font = ResolveFont(fonts);
        return new Vector2(0f, font.MeasureString("Ag").Y);
    }

    protected override int HitTestIndex(Vector2 screenPos)
    {
        var font = ResolveFont(IoCManager.Resolve<IFontManager>());
        var localX = screenPos.X - (Bounds.X + Padding.Left) + _scrollPixels;
        return HitTestColumn(Text, localX, font);
    }

    protected override (int start, int end) GetWordBounds(int index) => FindWordBounds(Text, index);

    protected override (int start, int end) GetTripleClickBounds(int index) => (0, Text.Length);

    // Keeps the caret visible by shifting scrollPixels just enough
    protected override void ClampScroll()
    {
        var font = ResolveFont(IoCManager.Resolve<IFontManager>());
        var caretX = font.MeasureString(Text[.._caret]).X;
        var visibleWidth = MathHelper.Max(0, Bounds.Width - Padding.Left - Padding.Right);

        if (caretX - _scrollPixels < 0)
            _scrollPixels = caretX;
        else if (caretX - _scrollPixels > visibleWidth)
            _scrollPixels = caretX - visibleWidth;

        _scrollPixels = MathHelper.Max(0, _scrollPixels);
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        base.DrawSelf(sb, fontManager, dt);

        _caretBlink += dt;
        if (_caretBlink >= CaretBlinkInterval * 2)
            _caretBlink = 0f;

        var font = ResolveFont(fontManager);
        var textOriginX = Bounds.X + Padding.Left - _scrollPixels;
        var textY = Bounds.Y + (Bounds.Height - font.MeasureString("Ag").Y) / 2f;

        var device = GameClient.GraphicsDevice;
        var previousScissor = device.ScissorRectangle;
        var clipped = Rectangle.Intersect(previousScissor, Bounds);
        if (clipped.Width <= 0 || clipped.Height <= 0)
            return;

        sb.End(); // scoped clip so overflowing/scrolled text can't bleed past our own Bounds
        device.ScissorRectangle = clipped;
        sb.Begin(rasterizerState: ScissorRasterizer);
        var textHeight = font.MeasureString("Ag").Y;

        if (_caret != _selectionAnchor)
        {
            var start = Math.Min(_caret, _selectionAnchor);
            var end = Math.Max(_caret, _selectionAnchor);
            var selX = textOriginX + font.MeasureString(Text[..start]).X;
            var selWidth = font.MeasureString(Text[start..end]).X;
            var selRect = new RectangleF(selX, textY, selWidth, textHeight);
            sb.FillRectangle(new Vector2(selRect.X, selRect.Y), new Vector2(selRect.Width, selRect.Height),
                new ColorGradient(SelectionColor).Resolve(selRect));
        }

        if (Text.Length > 0)
            sb.DrawString(font, Text, new Vector2(textOriginX, textY), Color);
        else if (!string.IsNullOrEmpty(PlaceholderText))
            sb.DrawString(font, PlaceholderText, new Vector2(textOriginX, textY), PlaceholderColor);

        if (IsFocused && _caretBlink < CaretBlinkInterval)
        {
            var caretX = textOriginX + font.MeasureString(Text[.._caret]).X;
            var caretHeight = CaretHeight > 0 ? CaretHeight : textHeight;
            var caretY = textY + (textHeight - caretHeight) / 2f;
            var caretRect = new RectangleF(caretX + CaretOffsetX, caretY + CaretOffsetY, CaretWidth, caretHeight);
            sb.FillRectangle(new Vector2(caretRect.X, caretRect.Y), new Vector2(caretRect.Width, caretRect.Height),
                new ColorGradient(CaretColor).Resolve(caretRect));
        }

        sb.End();
        device.ScissorRectangle = previousScissor;
        sb.Begin(rasterizerState: ScissorRasterizer);
    }
}
