using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    private Texture2D _pixel = default!;
    private Texture2D _circleTexture = default!;

    private static readonly Color DebugFill = new(0, 255, 0, 40);
    private static readonly Color DebugBorder = new(0, 255, 0, 150);

    internal void InitShapes(GraphicsDevice gd)
    {
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _circleTexture = GenerateCircleTexture(gd, 128);
    }

    public void DrawRect(Rectangle rect, Color? fillColor = null, Color? borderColor = null, int thickness = 1)
    {
        var fill = fillColor ?? DebugFill;
        var border = borderColor ?? DebugBorder;
        SubmitPixel(
            new Vector2(rect.Left, rect.Top),
            new Vector2(rect.Width, rect.Height),
            fill
        );

        DrawLine(new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Top), border, thickness);
        DrawLine(new Vector2(rect.Left, rect.Bottom), new Vector2(rect.Right, rect.Bottom), border, thickness);
        DrawLine(new Vector2(rect.Left, rect.Top), new Vector2(rect.Left, rect.Bottom), border, thickness);
        DrawLine(new Vector2(rect.Right, rect.Top), new Vector2(rect.Right, rect.Bottom), border, thickness);
    }

    public void FillRectImmediate(Rectangle rect, Color color)
    {
        _spriteBatch.Draw(_pixel, rect, color);
    }

    public void DrawCircle(Vector2 center, float radius, Color? fillColor = null, Color? borderColor = null)
    {
        var fill = fillColor ?? new Color(0, 200, 255, 40);
        var border = borderColor ?? new Color(0, 200, 255, 180);
        float diameter = radius * 2f;
        var fillRect = new TextureRect(_circleTexture)
        {
            Region = _circleTexture.Bounds,
            Color = fill,
            Origin = new Vector2(_circleTexture.Width / 2f, _circleTexture.Height / 2f),
            Scale = new Vector2(diameter / _circleTexture.Width, diameter / _circleTexture.Height),
            Layer = 9999,
            Depth = 0f,
        };
        Submit(fillRect, center);

        var borderRect = new TextureRect(_circleTexture)
        {
            Region = _circleTexture.Bounds,
            Color = border,
            Origin = new Vector2(_circleTexture.Width / 2f, _circleTexture.Height / 2f),
            Scale = new Vector2(diameter / _circleTexture.Width, diameter / _circleTexture.Height),
            Layer = 9999,
            Depth = 0f,
        };
        Submit(borderRect, center);
    }

    public void DrawPolygon(Vector2[] worldVerts, Color? fillColor = null, Color? borderColor = null, int thickness = 1)
    {
        var border = borderColor ?? new Color(255, 200, 0, 180);
        for (int i = 0; i < worldVerts.Length; i++)
        {
            var a = worldVerts[i];
            var b = worldVerts[(i + 1) % worldVerts.Length];
            DrawLine(a, b, border, thickness);
        }
    }

    public void DrawLine(Vector2 from, Vector2 to, Color color, int thickness = 1)
    {
        var diff = to - from;
        float length = diff.Length();
        float angle = MathF.Atan2(diff.Y, diff.X);

        var rect = new TextureRect(_pixel)
        {
            Region = _pixel.Bounds,
            Color = color,
            Rotation = angle,
            Origin = Vector2.Zero,
            Scale = new Vector2(length, thickness),
            Layer = 9999,
            Depth = 0f,
        };

        Submit(rect, from);
    }



    private void SubmitPixel(Vector2 position, Vector2 size, Color color)
    {
        var rect = new TextureRect(_pixel)
        {
            Region = _pixel.Bounds,
            Color = color,
            Origin = Vector2.Zero,
            Scale = size,
            Layer = 9999,
            Depth = 0f,
        };

        Submit(rect, position);
    }

    private static Texture2D GenerateCircleTexture(GraphicsDevice gd, int size)
    {
        var texture = new Texture2D(gd, size, size);
        var data = new Color[size * size];
        float center = size / 2f;
        float radiusSq = (size / 2f) * (size / 2f);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                data[y * size + x] = dx * dx + dy * dy <= radiusSq
                    ? Color.White
                    : Color.Transparent;
            }

        texture.SetData(data);
        return texture;
    }
}
