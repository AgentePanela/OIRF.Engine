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

    [StyleField("textDecoration", TextDecoration.None)]
    private TextDecoration? _textDecoration;

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

    private struct DisplayCache
    {
        public string? SourceText;
        public TTransform Transform;
        public bool Wrap;
        public float Width;
        public string? FontFamily;
        public float FontSize;
        public FontVariant FontVariant;
        public string? Result;
        public Vector2 Size;

        public readonly bool Matches(Label label, bool wrap, float width)
            => Result is not null
                && ReferenceEquals(SourceText, label.Text)
                && Transform == label.TextTransform
                && Wrap == wrap
                && (!wrap || Width == width)
                && ReferenceEquals(FontFamily, label.FontFamily)
                && FontSize == label.FontSize
                && FontVariant == label.FontVariant;
    }

    private DisplayCache _measureCache;
    private DisplayCache _drawCache;

    private (string Text, Vector2 Size) GetDisplayText(SpriteFontBase font, float maxWidth, bool wrap, bool forDraw)
    {
        ref var cache = ref forDraw ? ref _drawCache : ref _measureCache;
        if (cache.Matches(this, wrap, maxWidth))
            return (cache.Result!, cache.Size);

        var text = ApplyTransform(Text);
        if (wrap)
            text = new Label2D(font, text).WrapText(maxWidth);

        var size = MeasureString(font, text);

        cache = new DisplayCache
        {
            SourceText = Text,
            Transform = TextTransform,
            Wrap = wrap,
            Width = maxWidth,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontVariant = FontVariant,
            Result = text,
            Size = size,
        };
        return (text, size);
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
        var (_, size) = GetDisplayText(font, availableSize.X, wrap, forDraw: false);

        return size;
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        var font = ResolveFont(fontManager);
        var (text, textSize) = GetDisplayText(font, Bounds.Width, AutoWrap, forDraw: true);

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

        sb.DrawString(font, text, new Vector2(x, y), Color, textStyle: TextDecoration.ToTextStyle());
    }

    protected Vector2 MeasureString(SpriteFontBase font, string text)
    {
        var size = font.MeasureString(text);
        if (size == Vector2.Zero)
            return Vector2.One;
        
        return size;
    }
}
