using System.Collections.Generic;
using FontStashSharp;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Rasterizes and measures text from the engine's loaded font families
/// (<see cref="FontFamilyPrototype"/>).
/// </summary>
public interface IFontManager
{
    /// <summary>
    /// Every font family ID made via <see cref="FontFamilyPrototype"/>.
    /// </summary>
    IReadOnlyCollection<string> Families { get; }

    /// <summary>
    /// Gets a font from the default family (the first one loaded), rasterized at the given pixel size.
    /// </summary>
    SpriteFontBase Get(float size = FontManager.DefaultSize, FontVariant variant = FontVariant.Regular);

    /// <summary>
    /// Gets a font from a specific family, rasterized at the given pixel size. Falls back to
    /// that family's Regular if the requested variant isn't configured.
    /// </summary>
    SpriteFontBase Get(float size, string family, FontVariant variant = FontVariant.Regular);

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
    Vector2 Measure(string text, string family, float size, FontVariant variant = FontVariant.Regular);
}
