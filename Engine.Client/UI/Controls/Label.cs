using Apos.Shapes;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using SpriteFontBase = FontStashSharp.SpriteFontBase;

namespace Engine.Client.UI;

/// <summary>
/// Plain text control.
/// </summary>
public sealed partial class Label : Control
{
    /// <summary>
    /// Text drawn by this label. Not a style property - a theme has no business deciding content.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Text color.
    /// </summary>
    [StyleField("color", 0xFFFFFFFFu)]
    private Color? _color;

    /// <summary>
    /// Font family to draw with. Null falls back to the theme, then to IFontManager's default family.
    /// </summary>
    [StyleField("fontFamily")]
    private string? _fontFamily;

    [StyleField("fontSize", 16f)]
    private float? _fontSize;

    [StyleField("fontVariant", FontVariant.Regular)]
    private FontVariant? _fontVariant;

    /// <summary>
    /// If true, wraps Text onto multiple lines instead of overflowing the available width.
    /// </summary>
    [StyleField("autoWrap", false)]
    private bool? _autoWrap;

    private SpriteFontBase ResolveFont(IFontManager fonts)
        => FontFamily is null ? fonts.Get(FontSize, FontVariant) : fonts.Get(FontSize, FontFamily, FontVariant);

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var fonts = IoCManager.Resolve<IFontManager>();
        var font = ResolveFont(fonts);
        var text = Text ?? "";

        if (AutoWrap && !float.IsInfinity(availableSize.X))
            text = new Label2D(font, text).WrapText(availableSize.X);

        return font.MeasureString(text);
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        var font = ResolveFont(fontManager);
        var text = Text ?? "";

        if (AutoWrap)
            text = new Label2D(font, text).WrapText(Bounds.Width);

        sb.DrawString(font, text, new Vector2(Bounds.X, Bounds.Y), Color);
    }
}
