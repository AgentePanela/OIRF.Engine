using System.Collections.Generic;
using Engine.Shared.IoC;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Default boot catalog for engine font keys.
/// It tries multiple content asset names safely and falls back to Arial16.
/// </summary>
public static class DefaultFontCatalog
{
    private static readonly Dictionary<FontKey, string[]> _fontCandidates = new()
    {
        [FontKey.Default] = new[]
        {
            "Arial16"
        },

        [FontKey.UiBody] = new[]
        {
            "Fonts/UiBody",
            "Fonts/UIBody",
            "Fonts/Body",
            "Arial16"
        },

        [FontKey.UiTitle] = new[]
        {
            "Fonts/UiTitle",
            "Fonts/UITitle",
            "Fonts/Title",
            "Arial16"
        },

        [FontKey.Debug] = new[]
        {
            "Fonts/Debug",
            "Fonts/Mono",
            "Fonts/DebugMono",
            "Arial16"
        },

        [FontKey.Loading] = new[]
        {
            "Fonts/Loading",
            "Fonts/UiBody",
            "Arial16"
        },

        [FontKey.Tooltip] = new[]
        {
            "Fonts/Tooltip",
            "Fonts/UiBody",
            "Arial16"
        },

        [FontKey.Button] = new[]
        {
            "Fonts/Button",
            "Fonts/UiBody",
            "Arial16"
        },

        [FontKey.UiSmall] = new[]
        {
            "Fonts/UiSmall",
            "Fonts/Small",
            "Arial16"
        },

        [FontKey.Notification] = new[]
        {
            "Fonts/Notification",
            "Fonts/UiBody",
            "Arial16"
        },
    };

    public static string[] GetCandidates(FontKey key)
    {
        if (_fontCandidates.TryGetValue(key, out var candidates))
            return candidates;

        return new[] { "Arial16" };
    }

    public static void SetCandidates(FontKey key, params string[] assetNames)
    {
        if (assetNames is null || assetNames.Length == 0)
            return;

        _fontCandidates[key] = assetNames;
    }
}
