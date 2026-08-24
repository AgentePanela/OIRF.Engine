using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
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

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var wrap = AutoWrap && !float.IsInfinity(availableSize.X);
        return EnsureLayout(wrap ? availableSize.X : float.PositiveInfinity, wrap, forDraw: false).Size;
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        var layout = EnsureLayout(Bounds.Width, AutoWrap, forDraw: true);

        var blockY = TextVerticalAlign switch
        {
            VAlign.Center => Bounds.Y + (Bounds.Height - layout.Size.Y) / 2f,
            VAlign.Bottom => Bounds.Bottom - layout.Size.Y,
            _ => Bounds.Y, // top, stretch
        };

        foreach (var line in layout.Lines)
        {
            var x = TextAlign switch
            {
                HAlign.Center => Bounds.X + (Bounds.Width - line.Width) / 2f,
                HAlign.Right => Bounds.Right - line.Width,
                _ => Bounds.X, // left, stretch
            };

            var y = blockY + line.OffsetY;
            foreach (var run in line.Runs)
            {
                sb.DrawString(run.Font, run.Text, new Vector2(x, y), run.Color, textStyle: run.Style);
                x += run.Width;
            }
        }
    }
}
