using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Engine.Shared.IoC;
using Engine.Shared.Assets;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Default engine font manager. Fonts are rasterized on demand from the TTF
/// family loaded into <see cref="MyraFontSystem"/> - no content-pipeline .xnb needed.
/// </summary>
public sealed class FontManager : IFontManager
{
    private readonly Dictionary<FontKey, SpriteFontBase> _fonts = new();
    private bool _bootstrapped = false;

    [Dependency] private readonly TextStyleLibrary Styles = default!;

    /// <summary>
    /// Global font system containing font files added as game content. Files read directly.
    /// </summary>
    /// <remarks>All hail Myra!</remarks>
    public static readonly FontSystem MyraFontSystem = new();
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
            MyraFontSystem.AddFont(File.ReadAllBytes(file.FilePath));
        }
    }

    public void BootstrapDefaults()
    {
        if (_bootstrapped)
            return;

        Register(FontKey.Default, MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.Default)));
        Register(FontKey.UiBody, MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.UiBody)));
        Register(FontKey.UiTitle, MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.UiTitle)));
        Register(FontKey.Debug, MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.Debug)));
        Register(FontKey.Loading, MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.Loading)));
        Register(FontKey.Tooltip, MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.Tooltip)));
        Register(FontKey.Button, MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.Button)));
        Register(FontKey.UiSmall, MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.UiSmall)));
        Register(FontKey.Notification, MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.Notification)));

        _bootstrapped = true;
    }

    public void Register(FontKey key, SpriteFontBase font)
    {
        if (key == FontKey.None)
            return;

        _fonts[key] = font;
    }

    public bool Has(FontKey key)
        => key != FontKey.None && _fonts.ContainsKey(key);

    public SpriteFontBase Get(FontKey key)
    {
        if (key != FontKey.None && _fonts.TryGetValue(key, out var font))
            return font;

        return GetFallback();
    }

    public bool TryGet(FontKey key, [NotNullWhen(true)] out SpriteFontBase? font)
    {
        if (key != FontKey.None)
            return _fonts.TryGetValue(key, out font);

        font = null;
        return false;
    }

    public SpriteFontBase GetForStyle(TextStyle style)
    {
        if (style == TextStyle.None)
            return GetFallback();

        var def = Styles.Get(style);
        return MyraFontSystem.GetFont(def.Size);
    }

    public SpriteFontBase GetFallback()
    {
        if (_fonts.TryGetValue(FontKey.Default, out var fallback))
            return fallback;

        foreach (var kv in _fonts)
            return kv.Value;

        return MyraFontSystem.GetFont(DefaultFontSizes.Get(FontKey.Default));
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
