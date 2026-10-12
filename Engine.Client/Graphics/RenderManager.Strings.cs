using System;
using Engine.Client.Graphics.Batching;
using Engine.Client.Graphics.Fonts;
using FontStashSharp;
using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    [Dependency] private readonly IFontManager _fonts = default!;

    // glyphs are made for a few sizes only, or zooming smoothly would fill the font atlas with one set per frame
    private const float FontSizeStep = 2f;
    private const float MaxFontSize = 256f;

    private TextRenderer _textRenderer;

    // FontStashSharp works the glyphs out and hands each one over as a quad.
    private sealed class TextRenderer : IFontStashRenderer2
    {
        private readonly SpriteBatcher _batcher;

        public GraphicsDevice GraphicsDevice { get; }

        public TextRenderer(GraphicsDevice device, SpriteBatcher batcher)
        {
            GraphicsDevice = device;
            _batcher = batcher;
        }

        public void DrawQuad(Texture2D texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight,
            ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight)
            => _batcher.DrawQuad(texture, topLeft, topRight, bottomLeft, bottomRight);
    }

    private SpriteFontBase ResolveFont(Label2D label) => label.Font ?? _fonts.GetFallback();

    public void DrawString(Label2D label, Vector2 position)
    {
        var font = ResolveFont(label);
        var text = label.String ?? string.Empty;
        var origin = label.Origin;
        var scale = label.Scale;

        if (label.Pixelated)
        {
            _batcher.SetSampler(_commandSampler);
        }
        else
        {
            font = GetSharpFont(font, out var shrink);
            origin /= shrink;
            scale *= shrink;
            _batcher.SetSampler(SamplerState.LinearClamp);
        }

        if (label.OutlineEnabled)
            DrawOutline(font, text, position, origin, scale, label);

        if (label.ShadowEnabled)
            DrawText(font, text, position + label.ShadowOffset, label.ShadowColor, origin, scale, label);

        DrawText(font, text, position, label.Color, origin, scale, label, label.Decoration.ToTextStyle());
    }

    /// <summary>
    /// The same font at the size it ends up on the target, so its glyphs are drawn one texel per pixel instead of
    /// being stretched by the zoom and the render scale. 
    /// <paramref name="shrink"/> is what to scale the text by so it
    /// still takes the same room in the world.
    /// </summary>
    private SpriteFontBase GetSharpFont(SpriteFontBase font, out float shrink)
    {
        shrink = 1f;

        if (_current is not { } viewport || font is not DynamicSpriteFont dynamic)
            return font;

        var onTarget = font.FontSize * viewport.RenderScale * viewport.Camera.Zoom;
        var size = Math.Clamp(MathF.Round(onTarget / FontSizeStep) * FontSizeStep, FontSizeStep, MaxFontSize);
        if (size == font.FontSize)
            return font;

        shrink = font.FontSize / size;
        return dynamic.FontSystem.GetFont(size);
    }

    private void DrawText(SpriteFontBase font, string text, Vector2 position, Color color, Vector2 origin, Vector2 scale,
        Label2D label, TextStyle style = TextStyle.None)
    {
        font.DrawText(
            _textRenderer,
            text,
            position,
            color,
            rotation: label.Rotation,
            origin: origin,
            scale: scale,
            textStyle: style);
    }

    private void DrawOutline(SpriteFontBase font, string text, Vector2 position, Vector2 origin, Vector2 scale, Label2D label)
    {
        var thickness = label.OutlineThickness <= 0 ? 1 : label.OutlineThickness;

        for (var y = -thickness; y <= thickness; y++)
        {
            for (var x = -thickness; x <= thickness; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                DrawText(font, text, position + new Vector2(x, y), label.OutlineColor, origin, scale, label);
            }
        }
    }
}
