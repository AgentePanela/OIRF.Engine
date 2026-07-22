using FontStashSharp;
using Microsoft.Xna.Framework;
using System.Diagnostics.CodeAnalysis;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Central font registry and lookup service.
/// </summary>
public interface IFontManager
{
    /// <summary>
    /// Loads the engine's default font set if it was not loaded yet.
    /// This is safe to call multiple times.
    /// </summary>
    void BootstrapDefaults();

    /// <summary>
    /// Registers a font instance under a key.
    /// </summary>
    void Register(FontKey key, SpriteFontBase font);

    /// <summary>
    /// Checks whether the font key exists.
    /// </summary>
    bool Has(FontKey key);

    /// <summary>
    /// Gets a font by key. Returns fallback if not found.
    /// </summary>
    SpriteFontBase Get(FontKey key);

    /// <summary>
    /// Attempts to get a font by key.
    /// </summary>
    bool TryGet(FontKey key, [NotNullWhen(true)] out SpriteFontBase? font);

    /// <summary>
    /// Resolves the font used by a high-level text style.
    /// </summary>
    SpriteFontBase GetForStyle(TextStyle style);

    /// <summary>
    /// Gets the fallback/default engine font.
    /// </summary>
    SpriteFontBase GetFallback();

    /// <summary>
    /// Measures text using a specific registered font.
    /// </summary>
    Vector2 Measure(FontKey key, string text);

    /// <summary>
    /// Measures text using a specific style.
    /// </summary>
    Vector2 Measure(TextStyle style, string text);
}
