using Engine.Client.Graphics.Fonts;
using FontStashSharp;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    [Dependency] private readonly IFontManager _fonts = default!;

    private SpriteFontBase ResolveFont(Label2D label) => label.Font ?? _fonts.GetFallback();

    public void DrawString(Label2D label, Vector2 position)
    {
        var font = ResolveFont(label);
        var text = label.String ?? string.Empty;

        if (label.OutlineEnabled)
            DrawOutline(font, text, position, label);

        if (label.ShadowEnabled)
            DrawShadow(font, text, position, label);

        _spriteBatch.DrawString(
            font,
            text,
            position,
            label.Color,
            label.Rotation,
            label.Origin,
            label.Scale,
            textStyle: label.Decoration.ToTextStyle());
    }

    private void DrawShadow(SpriteFontBase font, string text, Vector2 position, Label2D label)
    {
        _spriteBatch.DrawString(
            font,
            text,
            position + label.ShadowOffset,
            label.ShadowColor,
            label.Rotation,
            label.Origin,
            label.Scale);
    }

    private void DrawOutline(SpriteFontBase font, string text, Vector2 position, Label2D label)
    {
        var thickness = label.OutlineThickness <= 0 ? 1 : label.OutlineThickness;

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
                    label.OutlineColor,
                    label.Rotation,
                    label.Origin,
                    label.Scale);
            }
        }
    }
}
