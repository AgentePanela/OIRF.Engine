using System.Collections.Generic;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.UI;

/// <summary>
/// Message-by-message rich text output, with no control about each messa ge.
/// </summary>
public partial class OutputPanel : PanelContainer
{
    private sealed class Entry(FormattedMessage message, Color? color)
    {
        public readonly FormattedMessage Message = message;
        public readonly Color? Color = color;
        public RichTextLayout? Layout;
        public float Top;
        public float Bottom => Top + (Layout?.Size.Y ?? 0f);
    }

    private readonly record struct LayoutKey(float Width, RichTextFont Font, float LineSeparation, float UIScale);

    [StyleField("color", 0xFFFFFFFFu)]
    private Color? _color;

    [StyleField("fontFamily")]
    private string? _fontFamily;

    [StyleField("fontSize", 16f)]
    private float? _fontSize;

    [StyleField("fontVariant", FontVariant.Regular)]
    private FontVariant? _fontVariant;

    [StyleField("lineSeparation", 2f)]
    private float? _lineSeparation;

    /// <summary>
    /// Pixels scrolled per wheel notch.
    /// </summary>
    public float ScrollSpeed { get; set; } = 50f;

    /// <summary>
    /// Whether a new message keeps the view pinned to the bottom, as long as it already was there.
    /// </summary>
    public bool ScrollFollowing { get; set; } = true;

    public int EntryCount => _entries.Count;

    private readonly List<Entry> _entries = new();
    private readonly ScrollBar _scrollBar = new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right };

    private Rectangle _contentRect;
    private LayoutKey? _layoutKey;
    private float _contentHeight;
    private bool _isAtBottom = true;

    public OutputPanel()
    {
        MouseFilter = MouseFilterMode.Pass;
        StyleAliasses.Add("label");
        StyleAliasses.Add("outputPanel");

        AddChild(_scrollBar);
        _scrollBar.OnValueChanged += _ => _isAtBottom = IsScrolledToEnd();
    }

    /// <summary>
    /// Appends a message. <paramref name="color"/> replaces the color for this message only.
    /// </summary>
    public void AddMessage(FormattedMessage message, Color? color = null)
    {
        var entry = new Entry(message, color);
        _entries.Add(entry);

        // no width yet - the first arrange lays everything out at once
        if (_layoutKey is not { } key)
            return;

        LayoutEntry(entry, _entries.Count - 1, key);
        _contentHeight = entry.Bottom;
        UpdateScrollRange();
    }

    /// <inheritdoc cref="AddMessage(FormattedMessage, Color?)"/>
    public void AddMarkup(string markup, Color? color = null) => AddMessage(FormattedMessage.Parse(markup), color);

    public void Clear()
    {
        _entries.Clear();
        _contentHeight = 0f;
        _isAtBottom = true;
        UpdateScrollRange();
    }

    public void ScrollToBottom()
    {
        _scrollBar.Value = float.MaxValue;
        _isAtBottom = true;
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        _scrollBar.Measure(availableSize);
        return Vector2.Zero; // grows with whatever space it's given, never with its messages
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        var panel = PanelRect(finalRect);
        var barWidth = (int)_scrollBar.DesiredSize.X;
        _scrollBar.Arrange(new Rectangle(panel.Right - barWidth, panel.Y, barWidth, panel.Height));

        // the bar width is always reserved, so it popping in dont rewrap every message
        _contentRect = new Rectangle(panel.X, panel.Y, MathHelper.Max(0, panel.Width - barWidth), panel.Height);
        EnsureLayout();
        UpdateScrollRange();
    }

    private bool EnsureLayout()
    {
        var key = new LayoutKey(
            _contentRect.Width,
            new RichTextFont(FontFamily, FontSize, FontVariant, Color),
            LineSeparation,
            IoCManager.Resolve<UIManager>().UIScale);

        if (_layoutKey == key)
            return false;

        _layoutKey = key;
        for (var i = 0; i < _entries.Count; i++)
            LayoutEntry(_entries[i], i, key);

        _contentHeight = _entries.Count > 0 ? _entries[^1].Bottom : 0f;
        return true;
    }

    private void LayoutEntry(Entry entry, int index, LayoutKey key)
    {
        var font = entry.Color is { } color ? key.Font with { Color = color } : key.Font;
        entry.Layout = RichTextLayout.Build(entry.Message, font, key.Width, wrap: true, key.UIScale);
        entry.Top = index == 0 ? 0f : _entries[index - 1].Bottom + key.LineSeparation;
    }

    private void UpdateScrollRange()
    {
        var follow = ScrollFollowing && _isAtBottom;

        _scrollBar.Page = _contentRect.Height;
        _scrollBar.MaxValue = MathHelper.Max(_contentHeight, _contentRect.Height);
        _scrollBar.Visible = _contentHeight > _contentRect.Height;

        if (follow)
            ScrollToBottom();
        else
            _scrollBar.Value = _scrollBar.Value; // re-clamps against the new range
    }

    private bool IsScrolledToEnd() => _scrollBar.Value >= _scrollBar.MaxValue - _scrollBar.Page - 0.5f;

    protected internal override bool MouseWheel(int delta)
    {
        var before = _scrollBar.Value;
        _scrollBar.Value -= delta / 120f * ScrollSpeed;
        return _scrollBar.Value != before;
    }

    // last entry whose top is at or above y
    private int FindEntryAt(float y)
    {
        int lo = 0, hi = _entries.Count - 1, found = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (_entries[mid].Top <= y)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }


    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        base.DrawSelf(sb, fontManager, dt);

        if (EnsureLayout())
            UpdateScrollRange();

        if (_entries.Count == 0 || _contentRect.Width <= 0 || _contentRect.Height <= 0)
            return;

        var device = GameClient.GraphicsDevice;
        var uiScale = IoCManager.Resolve<UIManager>().UIScale;
        var previousScissor = device.ScissorRectangle;
        var physicalContent = new Rectangle(
            (int)(_contentRect.X * uiScale), (int)(_contentRect.Y * uiScale),
            (int)(_contentRect.Width * uiScale), (int)(_contentRect.Height * uiScale));
        
        var clipped = Rectangle.Intersect(previousScissor, physicalContent);
        if (clipped.Width <= 0 || clipped.Height <= 0)
            return;

        sb.End();
        device.ScissorRectangle = clipped;
        UIBatch.Begin(sb, uiScale);

        var scroll = _scrollBar.Visible ? _scrollBar.Value : 0f;
        var originY = _contentRect.Y - scroll;
        var clipTop = (float)_contentRect.Y;
        var clipBottom = (float)_contentRect.Bottom;
        for (var i = FindEntryAt(scroll); i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var top = originY + entry.Top;
            if (top > clipBottom)
                break;

            entry.Layout!.Draw(sb, new Vector2(_contentRect.X, top), _contentRect.Width, HorizontalAlignment.Left, uiScale, clipTop, clipBottom);
        }

        sb.End();
        device.ScissorRectangle = previousScissor;
        UIBatch.Begin(sb, uiScale);
    }
}
