using System;
using System.Collections.Generic;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Engine.Shared.Assets;
using Engine.Shared.Storage;
using FontStashSharp.Rasterizers.FreeType;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Default engine font manager. Fonts are rasterized on demand from the TTF files loaded
/// into per-family <see cref="FontSystem"/> instances - no content-pipeline .xnb needed.
/// A family is just a .ttf's file name without extension; there's no further grouping (a
/// "Roboto-Regular.ttf" and "Roboto-Bold.ttf" are two separate families, not one with variants).
/// </summary>
public sealed class FontManager : IFontManager
{
    public const float DefaultSize = 16f;

    private static readonly Dictionary<string, FontSystem> _fontSystems = new();
    private static string? _defaultFamily;
    private static bool _loadedGameFonts;

    public readonly ResPath resPath = new("Fonts");

    public IReadOnlyCollection<string> Families => _fontSystems.Keys;

    public FontManager()
    {
        if (_loadedGameFonts)
            return; 

        FontSystemDefaults.TextShaper = new HarfBuzzTextShaper();
        FontSystemDefaults.FontLoader = new FreeTypeLoader();
        _loadedGameFonts = true;

        var ttfFiles = resPath.GetFiles("ttf");
        for (var index = 0; index < ttfFiles.Length; index++)
        {
            ref readonly var file = ref ttfFiles[index];
            var family = Path.GetFileNameWithoutExtension(file.Relative);

            if (!_fontSystems.TryGetValue(family, out var system))
            {
                system = new FontSystem();
                _fontSystems[family] = system;
                _defaultFamily ??= family;
                system.CurrentAtlasFull += (e, a) => system.Reset();
            }

            system.AddFont(FileSystem.ReadAllBytes(file.FilePath));
        }
    }

    public SpriteFontBase Get(float size = DefaultSize)
        => Get(size, _defaultFamily ?? string.Empty);

    public SpriteFontBase Get(float size, string family)
    {
        if (_fontSystems.TryGetValue(family, out var system))
            return system.GetFont(size);

        if (_defaultFamily is not null && _fontSystems.TryGetValue(_defaultFamily, out var fallback))
            return fallback.GetFont(size);

        throw new InvalidOperationException(
            $"No font family '{family}' loaded, and no fallback family available. " +
            $"Loaded families: {(Families.Count == 0 ? "(none)" : string.Join(", ", Families))}");
    }

    public SpriteFontBase GetFallback() => Get(DefaultSize);

    public Vector2 Measure(string text, float size) => Get(size).MeasureString(text ?? string.Empty);

    public Vector2 Measure(string text, string family, float size) => Get(size, family).MeasureString(text ?? string.Empty);
}
