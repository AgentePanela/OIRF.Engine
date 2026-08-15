using System;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Shared.Common;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SpriteFontBase = FontStashSharp.SpriteFontBase;

namespace Engine.Client.UI;

/// <summary>
/// Single-line text input.
/// </summary>
public partial class LineEdit : PanelContainer
{
    private const float CaretBlinkInterval = 0.5f;

    private static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };

    private string _text = "";

    /// <summary>
    /// The current text. Setting this Update the selection to the new length and fires
    /// <see cref="OnTextChanged"/>.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value)
                return;

            _text = value;
            _caret = Math.Clamp(_caret, 0, _text.Length);
            _selectionAnchor = Math.Clamp(_selectionAnchor, 0, _text.Length);
            OnTextChanged?.Invoke(_text);
        }
    }

    /// <summary>
    /// Shown (in <see cref="PlaceholderColor"/>) instead of the caret-less text when empty.
    /// </summary>
    public string PlaceholderText { get; set; } = "";

    /// <summary>
    /// Characters limit to the input text lenght.
    /// </summary>
    public int MaxLength { get; set; } = int.MaxValue;

    public event Action<string>? OnTextChanged;

    /// <summary>
    /// Fired when Enter is pressed while focused.
    /// </summary>
    public event Action<string>? OnTextEntered;

    [StyleField("color", 0xFFFFFFFFu)]
    private Color? _color;

    [StyleField("placeholderColor", 0x757575FFu)]
    private Color? _placeholderColor;

    [StyleField("selectionColor", 0x3390FF80u)]
    private Color? _selectionColor;

    [StyleField("caretColor", 0xFFFFFFFFu)]
    private Color? _caretColor;

    [StyleField("caretWidth", 0.1f)]
    private float? _caretWidth;

    /// <summary>
    /// Caret height in pixels. 0 to match the current font height.
    /// </summary>
    [StyleField("caretHeight", 0f)]
    private float? _caretHeight;

    [StyleField("caretOffsetX", 0f)]
    private float? _caretOffsetX;

    [StyleField("caretOffsetY", 0f)]
    private float? _caretOffsetY;

    [StyleField("fontFamily")]
    private string? _fontFamily;

    [StyleField("fontSize", 16f)]
    private float? _fontSize;

    [StyleField("fontVariant", FontVariant.Regular)]
    private FontVariant? _fontVariant;

    private int _caret;
    private int _selectionAnchor;
    private float _caretBlink;
    private float _scrollPixels;

    protected internal override bool WantsVirtualKeyboard => true;

    public LineEdit()
    {
        Focusable = true;
        MouseFilter = MouseFilterMode.Stop;
        StyleClasses.Add("lineEdit");
    }

    private SpriteFontBase ResolveFont(IFontManager fonts)
        => FontFamily is null ? fonts.Get(FontSize, FontVariant) : fonts.Get(FontSize, FontFamily, FontVariant);

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var fonts = IoCManager.Resolve<IFontManager>();
        var font = ResolveFont(fonts);
        return new Vector2(0f, font.MeasureString("Ag").Y);
    }

    private void MoveCaret(int newCaret, bool extendSelection)
    {
        _caret = Math.Clamp(newCaret, 0, Text.Length);
        if (!extendSelection)
            _selectionAnchor = _caret;
    }

    private void ResetBlink() => _caretBlink = 0f;

    /// <summary>
    /// Finds the character index closest to a screen-space X, accounting for the current
    /// horizontal scroll.
    /// </summary>
    private int HitTestCaret(float screenX)
    {
        var font = ResolveFont(IoCManager.Resolve<IFontManager>());
        var localX = screenX - (Bounds.X + Padding.Left) + _scrollPixels;

        if (localX <= 0)
            return 0;

        var previousWidth = 0f;
        for (var i = 1; i <= Text.Length; i++)
        {
            var width = font.MeasureString(Text[..i]).X;
            if (width >= localX)
                return localX - previousWidth < width - localX ? i - 1 : i;

            previousWidth = width;
        }

        return Text.Length;
    }

    // Keeps the caret visible by shifting scrollPixels just enough
    private void ClampScroll()
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
