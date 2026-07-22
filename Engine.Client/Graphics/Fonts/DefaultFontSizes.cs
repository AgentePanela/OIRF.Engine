using System.Collections.Generic;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Default pixel size used to rasterize each engine font key from
/// <see cref="FontManager.MyraFontSystem"/> when it isn't set explicitly.
/// </summary>
public static class DefaultFontSizes
{
    private static readonly Dictionary<FontKey, float> _sizes = new()
    {
        [FontKey.Default] = 16f,
        [FontKey.UiBody] = 16f,
        [FontKey.UiTitle] = 24f,
        [FontKey.Debug] = 13f,
        [FontKey.Loading] = 16f,
        [FontKey.Tooltip] = 14f,
        [FontKey.Button] = 16f,
        [FontKey.UiSmall] = 12f,
        [FontKey.Notification] = 15f,
    };

    public static float Get(FontKey key)
        => _sizes.TryGetValue(key, out var size) ? size : _sizes[FontKey.Default];

    public static void Set(FontKey key, float size)
        => _sizes[key] = size;
}
