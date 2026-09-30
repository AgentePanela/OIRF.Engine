using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using HAlign = Engine.Client.UI.HorizontalAlignment;
using VAlign = Engine.Client.UI.VerticalAlignment;

namespace Engine.Client.UI;

/// <summary>
/// Text control that understands BBCode markup
/// </summary>
public sealed partial class RichLabel : Control
{
    private string _text = "";

    /// <summary>
    /// Raw markup source. Setting this reparses via <see cref="FormattedMessage.Parse"/>.
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
            _message = FormattedMessage.Parse(_text);
            InvalidateLayout();
        }
    }

    private FormattedMessage _message = FormattedMessage.Parse("");

    /// <summary>
    /// 0.1-1f geral opacity
    /// </summary>
    public float Opacity { get; set; } = 1f;

    /// <inheritdoc cref="Label.Color"/>
    [StyleField("color", 0xFFFFFFFFu)]
    private Color? _color;

    /// <inheritdoc cref="Label.FontFamily"/>
    [StyleField("fontFamily")]
    private string? _fontFamily;

    /// <inheritdoc cref="Label.FontSize"/>
    [StyleField("fontSize", 16f)]
    private float? _fontSize;

    /// <inheritdoc cref="Label.FontVariant"/>
    [StyleField("fontVariant", FontVariant.Regular)]
    private FontVariant? _fontVariant;

    /// <inheritdoc cref="Label.AutoWrap"/>
    [StyleField("autoWrap", true)]
    private bool? _autoWrap;

    /// <inheritdoc cref="Label.TextAlign"/>
    [StyleField("textAlign", HAlign.Left)]
    private HAlign? _textAlign;

    /// <inheritdoc cref="Label.TextVerticalAlign"/>
    [StyleField("textVerticalAlign", VAlign.Top)]
    private VAlign? _textVerticalAlign;

    public RichLabel()
    {
        StyleAliasses.Add("label");
        StyleAliasses.Add("richTextLabel");
    }

    // Measure and Draw can legitimately be called with different maxWidth
    private struct LayoutCache
    {
        public FormattedMessage? Message;
        public bool Wrap;
        public float Width;
        public RichTextFont Font;
        public float UIScale;
        public RichTextLayout? Result;

        public readonly bool Matches(FormattedMessage message, RichTextFont font, bool wrap, float width, float uiScale)
            => Result is not null
                && ReferenceEquals(Message, message)
                && Wrap == wrap
                && (!wrap || Width == width)
                && Font == font
                && UIScale == uiScale;
    }

    private LayoutCache _measureCache;
    private LayoutCache _drawCache;

    private RichTextLayout EnsureLayout(float maxWidth, bool wrap, bool forDraw)
    {
        var uiScale = IoCManager.Resolve<UIManager>().UIScale;
        var font = new RichTextFont(FontFamily, FontSize, FontVariant, Color);
        ref var cache = ref forDraw ? ref _drawCache : ref _measureCache;
        if (cache.Matches(_message, font, wrap, maxWidth, uiScale))
            return cache.Result!;

        var result = RichTextLayout.Build(_message, font, maxWidth, wrap, uiScale);
        cache = new LayoutCache
        {
            Message = _message,
            Wrap = wrap,
            Width = maxWidth,
            Font = font,
            UIScale = uiScale,
            Result = result,
        };
        return result;
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var wrap = AutoWrap && !float.IsInfinity(availableSize.X);
        return EnsureLayout(wrap ? availableSize.X : float.PositiveInfinity, wrap, forDraw: false).Size;
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        var layout = EnsureLayout(Bounds.Width, AutoWrap, forDraw: true);
        var uiScale = IoCManager.Resolve<UIManager>().UIScale;

        var blockY = TextVerticalAlign switch
        {
            VAlign.Center => Bounds.Y + (Bounds.Height - layout.Size.Y) / 2f,
            VAlign.Bottom => Bounds.Bottom - layout.Size.Y,
            _ => Bounds.Y, // top, stretch
        };

        layout.Draw(sb, new Vector2(Bounds.X, blockY), Bounds.Width, TextAlign, uiScale, alpha: Opacity);
    }
}
