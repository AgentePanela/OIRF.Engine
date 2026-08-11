using System.Collections.Generic;
using FontStashSharp;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Rasterizes and measures text from the engine's loaded font families. Pure size/family
/// lookup - no notion of named/semantic styles, that lives one layer up.
/// </summary>
public interface IFontManager
{
    /// <summary>
    /// Every font family name loaded, derived from each .ttf's file name.
    /// </summary>
    IReadOnlyCollection<string> Families { get; }

    /// <summary>
    /// Gets a font from the default family (the first one loaded), rasterized at the given pixel size.
    /// </summary>
    SpriteFontBase Get(float size = FontManager.DefaultSize);

    /// <summary>
    /// Gets a font from a specific family, rasterized at the given pixel size.
    /// </summary>
    SpriteFontBase Get(float size, string family);

    /// <summary>
    /// Font used when no size/family was specified.
    /// </summary>
    SpriteFontBase GetFallback();

    /// <summary>
    /// Measures text as it would be drawn with the default family at the given pixel size.
    /// </summary>
    Vector2 Measure(string text, float size);

    /// <summary>
    /// Measures text as it would be drawn with a specific family at the given pixel size.
    /// </summary>
    Vector2 Measure(string text, string family, float size);
}
