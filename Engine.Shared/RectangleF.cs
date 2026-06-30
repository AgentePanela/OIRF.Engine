namespace Engine.Shared;

public struct RectangleF
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

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
}
