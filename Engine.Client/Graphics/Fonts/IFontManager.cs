using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
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
    void BootstrapDefaults(ContentManager content);

    /// <summary>
    /// Registers a font instance under a key.
    /// </summary>
    void Register(FontKey key, SpriteFont font);

    /// <summary>
    /// Loads a font from Content and registers it under a key.
    /// </summary>
    bool Load(ContentManager content, FontKey key, string assetName);

    /// <summary>
    /// Tries multiple asset names and registers the first one that exists.
    /// </summary>
    bool TryLoadFirstAvailable(ContentManager content, FontKey key, params string[] assetNames);

    /// <summary>
    /// Checks whether the font key exists.
    /// </summary>
    bool Has(FontKey key);

    /// <summary>
    /// Gets a font by key. Returns fallback if not found.
    /// </summary>
    SpriteFont Get(FontKey key);

    /// <summary>
    /// Attempts to get a font by key.
    /// </summary>
    bool TryGet(FontKey key, [NotNullWhen(true)] out SpriteFont? font);

    /// <summary>
    /// Resolves the font used by a high-level text style.
    /// </summary>
    SpriteFont GetForStyle(TextStyle style);

    /// <summary>
    /// Gets the fallback/default engine font.
    /// </summary>
    SpriteFont GetFallback();

    /// <summary>
    /// Measures text using a specific registered font.
    /// </summary>
    Vector2 Measure(FontKey key, string text);

    /// <summary>
    /// Measures text using a specific style.
    /// </summary>
    Vector2 Measure(TextStyle style, string text);
}
