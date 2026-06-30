using Microsoft.Xna.Framework;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI.Styles;

namespace Engine.Client.UI;

public static class UITheme
{
    public static Color BackgroundColor { get; set; } = new Color(25, 25, 25);
    public static Color ButtonColor { get; set; } = new Color(5, 5, 5);
    public static Color ButtonHoverColor { get; set; } = new Color(30, 30, 30);
    public static Color ButtonPressedColor { get; set; } = new Color(40, 40, 40);
    public static Color TextColor { get; set; } = Color.White;
    public static Color SelectionColor { get; set; } = new Color(176, 119, 56);
    public static Color? ButtonBorder { get; set; } = new (221, 221, 221, 85);

    private static SolidBrush Brush(Color c) => new(c);
    private static SolidBrush Darken(Color c, float factor = 0.8f)
        => new(new Color(c.ToVector3() * factor));

    /// <summary>
    /// Repaints the entire default Myra stylesheet using solid flat colors
    /// based on the current static properties.
    /// </summary>
    public static void ApplyFlatColors()
    {
        var style = Stylesheet.Current;

        ApplyWindow(style);
        ApplyButton(style);
        ApplyLabel(style);
        ApplyTextBox(style);
        ApplyTree(style);
        ApplyListBox(style);
        ApplyTabs(style);

        Stylesheet.Current = style;
    }

    private static void ApplyWindow(Stylesheet style)
    {
        if (style.WindowStyle is null) return;

        style.WindowStyle.Background = Brush(BackgroundColor);
        style.WindowStyle.Border = Darken(BackgroundColor);

        if (style.WindowStyle.TitleStyle is { } title)
        {
            title.Background = Darken(BackgroundColor);
            title.TextColor = TextColor;
        }
    }

    private static void ApplyButton(Stylesheet style)
    {
        if (style.ButtonStyle is null) return;

        style.ButtonStyle.Background = Brush(ButtonColor);
        style.ButtonStyle.OverBackground = Brush(ButtonHoverColor);
        style.ButtonStyle.PressedBackground = Brush(ButtonPressedColor);
        style.ButtonStyle.BorderThickness = new(2);
        style.ButtonStyle.Padding = new (5);

        if(ButtonBorder is not null)
            style.ButtonStyle.Border = Brush(ButtonBorder ?? Color.Transparent);

        if (style.ButtonStyle.LabelStyle is { } label)
            label.TextColor = TextColor;
    }

    private static void ApplyLabel(Stylesheet style)
    {
        if (style.LabelStyle is { } label)
            label.TextColor = TextColor;
    }

    private static void ApplyTextBox(Stylesheet style)
    {
        if (style.TextBoxStyle is { } tb)
        {
            tb.Padding = new (3);
            tb.BorderThickness = new (2);
            tb.Background = Brush(ButtonColor);
            if (ButtonBorder is not null)
                style.TextBoxStyle.Border = Brush(ButtonBorder ?? Color.Transparent);
        }
    }

    private static void ApplyTree(Stylesheet style)
    {
        if (style.TreeStyle is null) return;

        style.TreeStyle.SelectionBackground = Brush(SelectionColor);
        style.TreeStyle.SelectionHoverBackground = Darken(SelectionColor);
    }

    private static void ApplyListBox(Stylesheet style)
    {
        if (style.ListBoxStyle?.ListItemStyle is not { } item) return;

        item.PressedBackground = Brush(SelectionColor);
        item.OverBackground = Darken(SelectionColor);
    }

    private static void ApplyTabs(Stylesheet style)
    {
        if (style.TabControlStyle is null) return;

        style.TabControlStyle.Background = Brush(BackgroundColor);

        if (style.TabControlStyle.TabItemStyle is not { } tab) return;

        tab.Background = Brush(ButtonColor);
        tab.OverBackground = Brush(ButtonHoverColor);
        tab.PressedBackground = Brush(SelectionColor);
        tab.FocusedBorder = Brush(SelectionColor);

        if (tab.LabelStyle is { } label)
            label.TextColor = TextColor;
    }
}
