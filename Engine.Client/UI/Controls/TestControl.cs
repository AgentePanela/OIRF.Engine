using Microsoft.Xna.Framework;
using System;

namespace Engine.Client.UI.Controls;

public sealed class TestControl : PanelContainer
{
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

        VerticalAlignment = VerticalAlignment.Top;
        HorizontalAlignment = HorizontalAlignment.Left;
        MinHeight = 50;
        MinWidth = 50;
    }

    private static readonly Random _random = new();
    private bool GetRandomBool() => _random.Next(2) == 0;
    private Color GetRandomColor() => new(_random.Next(256), _random.Next(256), _random.Next(256));
}