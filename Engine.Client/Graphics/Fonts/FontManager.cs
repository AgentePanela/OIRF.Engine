using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using FontStashSharp;
using Engine.Shared.IoC;
using Engine.Shared.Assets;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Default engine font manager.
/// Keeps raw SpriteFont compatibility while enabling style-based lookup.
/// </summary>
public sealed class FontManager : IFontManager
{
    private readonly Dictionary<FontKey, SpriteFont> _fonts = new();
    private bool _bootstrapped = false;

    [Dependency] private readonly TextStyleLibrary Styles = default!;

    /// <summary>
    /// Global font system containing font files added as game content. Files read directly.
    /// </summary>
    /// <remarks>All hail Myra!</remarks>
    public static readonly FontSystem MyraFontSystem = new();
    private static readonly Dictionary<string, float> _fontEntries = [];
    private readonly bool loadedGameFonts = false;
    public readonly ResPath resPath = new("Fonts");

    public FontManager()
    {
        IoCManager.ResolveDependencies(this);

        if (loadedGameFonts) return; // Prevent excessive file loading.
        loadedGameFonts = true;

        var ttfFiles = resPath.GetFiles("ttf");

        for (var index = 0; index < ttfFiles.Length; index++)
        {
            ref readonly var file = ref ttfFiles[index];

            byte[] ttfData = File.ReadAllBytes(file.FilePath);

            MyraFontSystem.AddFont(ttfData);

            _fontEntries.TryAdd(
                Path.GetFileNameWithoutExtension(file.Relative),
                index
            );
        }
    }

    /// <summary>
    /// Gets a font entry from the Myra/FontStashSharp family of font management libraries.
    /// </summary>
    /// <param name="fontName">Name of font desired; reflected in file names w/o extension.</param>
    /// <returns>Returns a <see cref="DynamicSpriteFont"/></returns>
    public static DynamicSpriteFont? GetFont(string fontName)
        => _fontEntries.TryGetValue(fontName, out var fontIndex) ? MyraFontSystem.GetFont(fontIndex) : null;

    public void BootstrapDefaults(ContentManager content)
    {
        if (_bootstrapped)
            return;

        if (!Has(FontKey.Default))
            TryLoadFirstAvailable(content, FontKey.Default, DefaultFontCatalog.GetCandidates(FontKey.Default));

        var fallback = Get(FontKey.Default);

        BootstrapFont(content, FontKey.UiBody, fallback);
        BootstrapFont(content, FontKey.UiTitle, fallback);
        BootstrapFont(content, FontKey.Debug, fallback);
        BootstrapFont(content, FontKey.Loading, fallback);
        BootstrapFont(content, FontKey.Tooltip, fallback);
        BootstrapFont(content, FontKey.Button, fallback);
        BootstrapFont(content, FontKey.UiSmall, fallback);
        BootstrapFont(content, FontKey.Notification, fallback);

        _bootstrapped = true;
    }

    private void BootstrapFont(ContentManager content, FontKey key, SpriteFont fallback)
    {
        if (Has(key))
            return;

        if (!TryLoadFirstAvailable(content, key, DefaultFontCatalog.GetCandidates(key)))
            Register(key, fallback);
    }

    public void Register(FontKey key, SpriteFont font)
    {
        if (key == FontKey.None)
            return;

        _fonts[key] = font;
    }

    public bool Load(ContentManager content, FontKey key, string assetName)
    {
        if (key == FontKey.None)
            return false;

        if (_fonts.ContainsKey(key))
            return true;

        var font = content.Load<SpriteFont>(assetName);
        _fonts[key] = font;
        return true;
    }

    public bool TryLoadFirstAvailable(ContentManager content, FontKey key, params string[] assetNames)
    {
        if (key == FontKey.None)
            return false;

        if (_fonts.ContainsKey(key))
            return true;

        if (assetNames is null || assetNames.Length == 0)
            return false;

        foreach (string assetName in assetNames)
        {
            if (string.IsNullOrWhiteSpace(assetName))
                continue;

            try
            {
                var font = content.Load<SpriteFont>(assetName);
                _fonts[key] = font;
                return true;
            }
            catch
            {
                Log.Error($"SpriteFont {assetName} does not exist!");
            }
        }

        return false;
    }

    public bool Has(FontKey key)
        => key != FontKey.None && _fonts.ContainsKey(key);

    public SpriteFont Get(FontKey key)
    {
        if (key != FontKey.None && _fonts.TryGetValue(key, out var font))
            return font;

        return GetFallback();
    }

    public bool TryGet(FontKey key, [NotNullWhen(true)] out SpriteFont? font)
    {
        if (key != FontKey.None)
            return _fonts.TryGetValue(key, out font);

        font = null;
        return false;
    }

    public SpriteFont GetForStyle(TextStyle style)
    {
        if (style == TextStyle.None)
            return GetFallback();

        var def = Styles.Get(style);
        return Get(def.FontKey);
    }

    public SpriteFont GetFallback()
    {
        if (_fonts.TryGetValue(FontKey.Default, out var fallback))
            return fallback;

        foreach (var kv in _fonts)
            return kv.Value;

        throw new System.InvalidOperationException("No fonts are registered in FontManager.");
    }

    public Vector2 Measure(FontKey key, string text)
    {
        text ??= string.Empty;
        return Get(key).MeasureString(text);
    }

    public Vector2 Measure(TextStyle style, string text)
    {
        text ??= string.Empty;
        return GetForStyle(style).MeasureString(text);
    }
}
