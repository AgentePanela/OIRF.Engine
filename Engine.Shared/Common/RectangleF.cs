using Microsoft.Xna.Framework;

namespace Engine.Shared.Common;

/// <inheritdoc cref="Rectangle"/>
public struct RectangleF
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width {get; set; }
    public float Height { get; set; }

    public float Left => X;
    public float Right => X + Width;
    public float Top => Y;
    public float Bottom => Y + Height;

    public RectangleF(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public RectangleF(Rectangle r) : this(r.X, r.Y, r.Width, r.Height)
    {
        
    }

    public static RectangleF ToRectF(Rectangle r) => new();
}
