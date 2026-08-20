using System;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using SpriteFontBase = FontStashSharp.SpriteFontBase;

namespace Engine.Client.UI;

/// <summary>
/// Multi-line text input.
/// </summary>
public sealed partial class TextEdit : BaseTextInput
{
    private int _rows = 4;

    /// <summary>
    /// How many lines tall this box measures, regardless of how much text it holds.
    /// </summary>
    public int Rows
    {
        get => _rows;
        set => SetLayoutField(ref _rows, value);
    }

    /// <summary>
    /// Fired when Ctrl+Enter is pressed while focused
    /// </summary>
    public event Action<string>? OnTextEntered;

    /// <summary>
    /// Extra pixels of gap between lines, on top of the font height.
    /// </summary>
    [StyleField("lineSpacing", 0f)]
    private float? _lineSpacing;

    /// <summary>
    /// Warp long lines into multiple visual rows instead of scrolling horizontally.
    /// </summary>
    [StyleField("autoWrap", true)]
    private bool? _autoWrap;

    private float _scrollPixelsY;
    private float? _caretPreferredX;

    private readonly ScrollBar _vScrollBar = new()
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Stretch,
        Margin = new Thickness(0)
    };

    private float _scrollBarReserve;

    public TextEdit()
    {
        StyleAliasses.Add("textEdit");

        AddChild(_vScrollBar);
        _vScrollBar.OnValueChanged += v => _scrollPixelsY = v;
    }

    protected override void OnCaretChanged() => _caretPreferredX = null;

    private float LineHeight(SpriteFontBase font) => font.MeasureString("Ag").Y + LineSpacing;

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        var fonts = IoCManager.Resolve<IFontManager>();
        var font = ResolveFont(fonts);
        _vScrollBar.Measure(availableSize);

        // Fixed Rows-tall box regardless of content
        return new Vector2(0f, LineHeight(font) * Rows);
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        var panelRect = PanelRect(finalRect);
        var font = ResolveFont(IoCManager.Resolve<IFontManager>());
        var lineHeight = LineHeight(font);

        var visualLineCount = GetVisualLines().Count;
        var totalHeight = visualLineCount * lineHeight;
        var visibleHeight = MathHelper.Max(0, panelRect.Height - Padding.Top - Padding.Bottom);
        var maxScroll = MathHelper.Max(0, totalHeight - visibleHeight);

        _vScrollBar.MaxValue = totalHeight;
        _vScrollBar.Page = visibleHeight;
        _vScrollBar.Visible = maxScroll > 0;
        _scrollBarReserve = _vScrollBar.Visible ? _vScrollBar.DesiredSize.X : 0f;

        _vScrollBar.Arrange(new Rectangle(
            panelRect.Right - (int)_vScrollBar.DesiredSize.X, panelRect.Y, (int)_vScrollBar.DesiredSize.X, panelRect.Height));

        // content/viewport may have changed size since the last scroll
        _scrollPixelsY = MathHelper.Clamp(_scrollPixelsY, 0, maxScroll);
        _vScrollBar.Value = _scrollPixelsY;
    }
}
