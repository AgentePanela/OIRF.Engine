using Apos.Shapes;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using HAlign = Engine.Client.UI.HorizontalAlignment;
using VAlign = Engine.Client.UI.VerticalAlignment;
using TTransform = Engine.Client.UI.TextTransform;
using SpriteFontBase = FontStashSharp.SpriteFontBase;

namespace Engine.Client.UI;

/// <summary>
/// Plain text control.
/// </summary>
public partial class Label : Control
{
    private string _text = "";

    /// <summary>
    /// Text drawn by this label.
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
            InvalidateLayout();
        }
    }

    /// <summary>
    /// Text color.
    /// </summary>
    [StyleField("color", 0xFFFFFFFFu)]
    private Color? _color;

    /// <summary>
    /// Font family to draw with.
    /// </summary>
    [StyleField("fontFamily")]
    private string? _fontFamily;

    [StyleField("fontSize", 16f)]
    private float? _fontSize;

    [StyleField("fontVariant", FontVariant.Regular)]
    private FontVariant? _fontVariant;

    /// <summary>
    /// Wraps Text onto multiple lines instead of overflowing the available width.
    /// </summary>
    [StyleField("autoWrap", true)]
    private bool? _autoWrap;

    /// <summary>
    /// How the text lines up horizontally within own Bounds.
    /// </summary>
    [StyleField("textAlign", HAlign.Left)]
    private HAlign? _textAlign;

    /// <summary>
    /// How the text lines up vertically within own Bounds.
    /// </summary>
    [StyleField("textVerticalAlign", VAlign.Top)]
    private VAlign? _textVerticalAlign;

    /// <summary>
    /// Casing applied to Text before measuring/drawing - equivalent to CSS text-transform.
    /// </summary>
    [StyleField("textTransform", TTransform.None)]
    private TTransform? _textTransform;

    private SpriteFontBase ResolveFont(IFontManager fonts)
        => FontFamily is null ? fonts.Get(FontSize, FontVariant) : fonts.Get(FontSize, FontFamily, FontVariant);

    private string? _displayCacheSourceText;
    private TTransform _displayCacheTransform;
    private bool _displayCacheWrap;
    private float _displayCacheWidth;
    private string? _displayCacheFontFamily;
    private float _displayCacheFontSize;
    private FontVariant _displayCacheFontVariant;
    private string? _displayCacheResult;

    private string GetDisplayText(SpriteFontBase font, float maxWidth, bool wrap)
    {
        if (_displayCacheResult is not null
            && ReferenceEquals(_displayCacheSourceText, Text)
            && _displayCacheTransform == TextTransform
            && _displayCacheWrap == wrap
            && (!wrap || _displayCacheWidth == maxWidth)
            && ReferenceEquals(_displayCacheFontFamily, FontFamily)
            && _displayCacheFontSize == FontSize
            && _displayCacheFontVariant == FontVariant)
        {
            return _displayCacheResult;
        }

        var text = ApplyTransform(Text);
        if (wrap)
            text = new Label2D(font, text).WrapText(maxWidth);

        _displayCacheSourceText = Text;
        _displayCacheTransform = TextTransform;
        _displayCacheWrap = wrap;
        _displayCacheWidth = maxWidth;
        _displayCacheFontFamily = FontFamily;
        _displayCacheFontSize = FontSize;
        _displayCacheFontVariant = FontVariant;
        _displayCacheResult = text;
        return text;
    }

    private string ApplyTransform(string text) => TextTransform switch
    {
        TTransform.Uppercase => text.ToUpperInvariant(),
        TTransform.Lowercase => text.ToLowerInvariant(),
        TTransform.Capitalize => CapitalizeWords(text),
        _ => text,
    };

    private static string CapitalizeWords(string text)
    {
        if (text.Length == 0)
            return text;

        var chars = text.ToCharArray();
        var capitalizeNext = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i]))
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                capitalizeNext = false;
            }
        }

        return new string(chars);
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var fonts = IoCManager.Resolve<IFontManager>();
        var font = ResolveFont(fonts);
        var wrap = AutoWrap && !float.IsInfinity(availableSize.X);
        var text = GetDisplayText(font, availableSize.X, wrap);

        return MeasureString(font, text);
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        var font = ResolveFont(fontManager);
        var text = GetDisplayText(font, Bounds.Width, AutoWrap);
        var textSize = MeasureString(font, text);

        var x = TextAlign switch
        {
            HAlign.Center => Bounds.X + (Bounds.Width - textSize.X) / 2f,
            HAlign.Right => Bounds.Right - textSize.X,
            _ => Bounds.X, // Left, Stretch
        };

        var y = TextVerticalAlign switch
        {
            VAlign.Center => Bounds.Y + (Bounds.Height - textSize.Y) / 2f,
            VAlign.Bottom => Bounds.Bottom - textSize.Y,
            _ => Bounds.Y, // Top, Stretch
        };

        sb.DrawString(font, text, new Vector2(x, y), Color);
    }

    protected Vector2 MeasureString(SpriteFontBase font, string text)
    {
        var size = font.MeasureString(text);
        if (size == Vector2.Zero)
            return Vector2.One;
        
        return size;
    }
}
