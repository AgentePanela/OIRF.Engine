using Engine.Client.Graphics.Fonts;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TextStyle = Engine.Client.Graphics.Fonts.TextStyle;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    [Dependency] private readonly IFontManager _fonts = default!;
    [Dependency] private readonly TextStyleLibrary _styles = default!;

    /*
        This font system is causing a lot of blot in render manager and does not use resources path, maybe revert and recreate?
        Commits hash if it will get reverted one day: 
            77248cf6501c262a6e550a8a5dae1ebff8e76f71, f0219d37c31972b102c241c3bd1a944405a04076, 5fc64c8936cb758a14fda624c6777b39ad953050, this commit too
    */

    private SpriteFontBase ResolveFont(Label2D label)
    {
        if (label.Font is not null)
            return label.Font;

        if (label.Style != TextStyle.None)
            return _fonts.GetForStyle(label.Style);

        if (label.FontKey != FontKey.None)
            return _fonts.Get(label.FontKey);

        return _fonts.GetFallback();
    }

    private TextStyleDefinition? ResolveStyleDefinition(Label2D label)
    {
        if (label.Style == TextStyle.None)
            return null;

        if (!_styles.Has(label.Style))
            return null;

        return _styles.Get(label.Style);
    }

    private Color ResolveColor(Label2D label, TextStyleDefinition? styleDef)
    {
        if (label.UseStyleColor && styleDef is not null)
            return styleDef.Color;

        return label.Color;
    }

    private Vector2 ResolveScale(Label2D label, TextStyleDefinition? styleDef)
    {
        if (label.UseStyleScale && styleDef is not null)
            return label.Scale * styleDef.Scale;

        return label.Scale;
    }

    private bool ResolveShadowEnabled(Label2D label, TextStyleDefinition? styleDef)
    {
        if (label.UseStyleEffects && styleDef is not null)
            return styleDef.ShadowEnabled;

        return label.ShadowEnabled;
    }

    private Color ResolveShadowColor(Label2D label, TextStyleDefinition? styleDef)
    {
        if (label.UseStyleEffects && styleDef is not null)
            return styleDef.ShadowColor;

        return label.ShadowColor;
    }

    private Vector2 ResolveShadowOffset(Label2D label, TextStyleDefinition? styleDef)
    {
        if (label.UseStyleEffects && styleDef is not null)
            return styleDef.ShadowOffset;

        return label.ShadowOffset;
    }

    private bool ResolveOutlineEnabled(Label2D label, TextStyleDefinition? styleDef)
    {
        if (label.UseStyleEffects && styleDef is not null)
            return styleDef.OutlineEnabled;

        return label.OutlineEnabled;
    }

    private Color ResolveOutlineColor(Label2D label, TextStyleDefinition? styleDef)
    {
        if (label.UseStyleEffects && styleDef is not null)
            return styleDef.OutlineColor;

        return label.OutlineColor;
    }

    private int ResolveOutlineThickness(Label2D label, TextStyleDefinition? styleDef)
    {
        if (label.UseStyleEffects && styleDef is not null)
            return styleDef.OutlineThickness;

        return label.OutlineThickness;
    }

    private void DrawShadow(
        SpriteFontBase font,
        string text,
        Vector2 position,
        Label2D label,
        Vector2 scale,
        Color shadowColor,
        Vector2 shadowOffset)
    {
        _spriteBatch.DrawString(
            font,
            text,
            position + shadowOffset,
            shadowColor,
            label.Rotation,
            label.Origin,
            scale,
            label.Depth);
    }

    private void DrawOutline(
        SpriteFontBase font,
        string text,
        Vector2 position,
        Label2D label,
        Vector2 scale,
        Color outlineColor,
        int thickness)
    {
        if (thickness <= 0)
            thickness = 1;

        for (var y = -thickness; y <= thickness; y++)
        {
            for (var x = -thickness; x <= thickness; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                _spriteBatch.DrawString(
                    font,
                    text,
                    position + new Vector2(x, y),
                    outlineColor,
                    label.Rotation,
                    label.Origin,
                    scale,
                    label.Depth);
            }
        }
    }
}
