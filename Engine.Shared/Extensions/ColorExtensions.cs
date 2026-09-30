using System;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Extensions;

public static class ColorExtensions
{
    /// <summary>
    /// The color as "#RRGGBB", or "#RRGGBBAA".
    /// </summary>
    public static string ToHex(this Color color)
        => color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    /// <summary>
    /// Parte color from "#RRGGBB", or "#RRGGBBAA". Returns black if invalid!!
    /// </summary>
    public static Color FromHex(this string hex)
    {
        hex = hex.TrimStart('#');

        if (hex.Length != 6 && hex.Length != 8)
            return Color.Black;

        byte r = Convert.ToByte(hex[0..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        byte a = hex.Length == 8 ? Convert.ToByte(hex[6..8], 16) : (byte)255;

        return new Color(r, g, b, a);
    }
}
