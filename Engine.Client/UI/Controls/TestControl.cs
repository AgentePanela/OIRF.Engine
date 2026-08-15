using Engine.Client.Inputs;
using Microsoft.Xna.Framework;
using System;

namespace Engine.Client.UI;

public sealed partial class TestControl : PanelContainer
{
    private ColorGradient? _normalOutline;
    private Thickness _normalOutlineThickness;

    public TestControl()
    {
        Background = GetRandomBool()
            ? ColorGradient.Diagonal(GetRandomColor(), GetRandomColor())
            : GetRandomColor();

        if (GetRandomBool())
        {
            OutlineColor = GetRandomBool()
                ? ColorGradient.Diagonal(GetRandomColor(), GetRandomColor())
                : GetRandomColor();
            OutlineThickness = new(2);
        }

        _normalOutline = OutlineColor;
        _normalOutlineThickness = OutlineThickness;

        VerticalAlignment = VerticalAlignment.Top;
        HorizontalAlignment = HorizontalAlignment.Left;
        MinHeight = 50;
        MinWidth = 50;

        MouseFilter = MouseFilterMode.Stop;
        Focusable = true;

        AddChild(new Label
        {
            Text = "Test\nControl",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
    }

    protected internal override void MouseEntered()
    {
        OutlineColor = Color.White;
        OutlineThickness = new Thickness(4);
    }

    protected internal override void MouseExited()
    {
        OutlineColor = _normalOutline;
        OutlineThickness = _normalOutlineThickness;
    }

    protected internal override void Click(MouseButton button)
    {
        Background = GetRandomBool()
            ? ColorGradient.Diagonal(GetRandomColor(), GetRandomColor())
            : GetRandomColor();
    }

    protected override void FocusChanged(bool focused)
    {
        if (focused)
        {
            OutlineColor = Color.Yellow;
            OutlineThickness = new Thickness(4);
        }
        else if (!IsMouseInside)
        {
            OutlineColor = _normalOutline;
            OutlineThickness = _normalOutlineThickness;
        }
    }

    private static readonly Random _random = new();
    private bool GetRandomBool() => _random.Next(2) == 0;
    private Color GetRandomColor() => new(_random.Next(256), _random.Next(256), _random.Next(256));
}