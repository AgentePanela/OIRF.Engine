using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Engine.Client.Graphics.Fonts;

/// <summary>
/// Central style map used by the font manager and UI text helpers.
/// </summary>
public sealed class TextStyleLibrary
{
    private readonly Dictionary<TextStyle, TextStyleDefinition> _styles = new();

    public TextStyleLibrary()
    {
        _styles[TextStyle.Body] = new TextStyleDefinition(
            FontKey.UiBody,
            Color.White,
            1f);

        _styles[TextStyle.Title] = new TextStyleDefinition(
            FontKey.UiTitle,
            Color.White,
            1f)
        {
            ShadowEnabled = true,
            ShadowColor = new Color(0, 0, 0, 180),
            ShadowOffset = new Vector2(2f, 2f)
        };

        _styles[TextStyle.Debug] = new TextStyleDefinition(
            FontKey.Debug,
            Color.White,
            1f)
        {
            ShadowEnabled = true,
            ShadowColor = new Color(0, 0, 0, 160),
            ShadowOffset = new Vector2(1f, 1f)
        };

        _styles[TextStyle.Loading] = new TextStyleDefinition(
            FontKey.Loading,
            Color.White,
            1f)
        {
            ShadowEnabled = true,
            ShadowColor = new Color(0, 0, 0, 160),
            ShadowOffset = new Vector2(1f, 1f)
        };

        _styles[TextStyle.Tooltip] = new TextStyleDefinition(
            FontKey.Tooltip,
            Color.White,
            1f)
        {
            OutlineEnabled = true,
            OutlineColor = new Color(0, 0, 0, 160),
            OutlineThickness = 1
        };

        _styles[TextStyle.Button] = new TextStyleDefinition(
            FontKey.Button,
            Color.White,
            1f);

        _styles[TextStyle.ButtonText] = new TextStyleDefinition(
            FontKey.Button,
            Color.White,
            1f)
        {
            ShadowEnabled = true,
            ShadowColor = new Color(0, 0, 0, 140),
            ShadowOffset = new Vector2(1f, 1f)
        };

        _styles[TextStyle.Caption] = new TextStyleDefinition(
            FontKey.UiSmall,
            Color.LightGray,
            1f);

        _styles[TextStyle.Notification] = new TextStyleDefinition(
            FontKey.Notification,
            Color.White,
            1f)
        {
            OutlineEnabled = true,
            OutlineColor = new Color(0, 0, 0, 180),
            OutlineThickness = 1
        };
    }

    public bool Has(TextStyle style)
        => _styles.ContainsKey(style);

    public TextStyleDefinition Get(TextStyle style)
    {
        if (_styles.TryGetValue(style, out var def))
            return def;

        return _styles[TextStyle.Body];
    }

    public void Set(TextStyle style, TextStyleDefinition definition)
    {
        _styles[style] = definition;
    }
}
