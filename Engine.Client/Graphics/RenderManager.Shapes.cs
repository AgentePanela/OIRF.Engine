using Apos.Shapes;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    public void DrawRect(Rectangle rect, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, CornerRadii rounded = default, float rotation = 0f, bool unshaded = false)
    {
        SubmitShape(new RectRenderable
        {
            XY = new Vector2(rect.Left, rect.Top),
            Size = new Vector2(rect.Width, rect.Height),
            Fill = fillColor ?? Color.Transparent,
            Border = borderColor ?? Color.Transparent,
            Thickness = thickness,
            Rounded = rounded,
            Rotation = rotation,
            Unshaded = unshaded,
        }, Vector2.Zero);
    }

    public void FillRectImmediate(Rectangle rect, Gradient color)
    {
        GameClient.ShapeBatch.FillRectangle(new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height), color);
    }

    public void DrawCircle(Vector2 center, float radius, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rotation = 0f, bool unshaded = false)
    {
        SubmitShape(new CircleRenderable
        {
            Center = center,
            Radius = radius,
            Fill = fillColor ?? Color.Transparent,
            Border = borderColor ?? Color.Transparent,
            Thickness = thickness,
            Rotation = rotation,
            Unshaded = unshaded,
        }, Vector2.Zero);
    }

    public void DrawEllipse(Vector2 center, float radiusX, float radiusY, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rotation = 0f, bool unshaded = false)
    {
        SubmitShape(new EllipseRenderable
        {
            Center = center,
            RadiusX = radiusX,
            RadiusY = radiusY,
            Fill = fillColor ?? Color.Transparent,
            Border = borderColor ?? Color.Transparent,
            Thickness = thickness,
            Rotation = rotation,
            Unshaded = unshaded,
        }, Vector2.Zero);
    }

    public void DrawLine(Vector2 from, Vector2 to, float radius, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0f, bool unshaded = false)
    {
        SubmitShape(new LineRenderable
        {
            A = from,
            B = to,
            Radius = radius,
            Fill = fillColor ?? Color.Transparent,
            Border = borderColor ?? Color.Transparent,
            Thickness = thickness,
            Unshaded = unshaded,
        }, Vector2.Zero);
    }

    public void DrawHexagon(Vector2 center, float radius, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rounded = 0f, float rotation = 0f, bool unshaded = false)
    {
        SubmitShape(new HexagonRenderable
        {
            Center = center,
            Radius = radius,
            Fill = fillColor ?? Color.Transparent,
            Border = borderColor ?? Color.Transparent,
            Thickness = thickness,
            Rounded = rounded,
            Rotation = rotation,
            Unshaded = unshaded,
        }, Vector2.Zero);
    }

    public void DrawEquilateralTriangle(Vector2 center, float radius, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rounded = 0f, float rotation = 0f, bool unshaded = false)
    {
        SubmitShape(new EquilateralTriangleRenderable
        {
            Center = center,
            Radius = radius,
            Fill = fillColor ?? Color.Transparent,
            Border = borderColor ?? Color.Transparent,
            Thickness = thickness,
            Rounded = rounded,
            Rotation = rotation,
            Unshaded = unshaded,
        }, Vector2.Zero);
    }

    public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, float rounded = 0f, bool unshaded = false)
    {
        SubmitShape(new TriangleRenderable
        {
            A = a,
            B = b,
            C = c,
            Fill = fillColor ?? Color.Transparent,
            Border = borderColor ?? Color.Transparent,
            Thickness = thickness,
            Rounded = rounded,
            Unshaded = unshaded,
        }, Vector2.Zero);
    }

    public void DrawArc(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, bool unshaded = false)
    {
        SubmitShape(new ArcRenderable
        {
            Center = center,
            Angle1 = angle1,
            Angle2 = angle2,
            Radius1 = radius1,
            Radius2 = radius2,
            Fill = fillColor ?? Color.Transparent,
            Border = borderColor ?? Color.Transparent,
            Thickness = thickness,
            Unshaded = unshaded,
        }, Vector2.Zero);
    }

    public void DrawRing(Vector2 center, float angle1, float angle2, float radius1, float radius2, Gradient? fillColor = null, Gradient? borderColor = null, float thickness = 0, bool unshaded = false)
    {
        SubmitShape(new RingRenderable
        {
            Center = center,
            Angle1 = angle1,
            Angle2 = angle2,
            Radius1 = radius1,
            Radius2 = radius2,
            Fill = fillColor ?? Color.Transparent,
            Border = borderColor ?? Color.Transparent,
            Thickness = thickness,
            Unshaded = unshaded,
        }, Vector2.Zero);
    }

    public void DrawPolygon(Vector2[] worldVerts, Gradient? borderColor = null, float thickness = 1, bool unshaded = false)
    {
        var border = borderColor ?? Color.Transparent;
        for (int i = 0; i < worldVerts.Length; i++)
        {
            var a = worldVerts[i];
            var b = worldVerts[(i + 1) % worldVerts.Length];
            DrawLine(a, b, thickness / 2f, border, unshaded: unshaded);
        }
    }

    internal void DrawRectShape(RectRenderable r)
        => _shapeBatch.DrawRectangle(r.XY, r.Size, r.Fill, r.Border, r.Thickness, r.Rounded, r.Rotation);

    internal void DrawCircleShape(CircleRenderable r)
        => _shapeBatch.DrawCircle(r.Center, r.Radius, r.Fill, r.Border, r.Thickness, r.Rotation);

    internal void DrawEllipseShape(EllipseRenderable r)
        => _shapeBatch.DrawEllipse(r.Center, r.RadiusX, r.RadiusY, r.Fill, r.Border, r.Thickness, r.Rotation);

    internal void DrawLineShape(LineRenderable r)
        => _shapeBatch.DrawLine(r.A, r.B, r.Radius, r.Fill, r.Border, r.Thickness);

    internal void DrawHexagonShape(HexagonRenderable r)
        => _shapeBatch.DrawHexagon(r.Center, r.Radius, r.Fill, r.Border, r.Thickness, r.Rounded, r.Rotation);

    internal void DrawEquilateralTriangleShape(EquilateralTriangleRenderable r)
        => _shapeBatch.DrawEquilateralTriangle(r.Center, r.Radius, r.Fill, r.Border, r.Thickness, r.Rounded, r.Rotation);

    internal void DrawTriangleShape(TriangleRenderable r)
        => _shapeBatch.DrawTriangle(r.A, r.B, r.C, r.Fill, r.Border, r.Thickness, r.Rounded);

    internal void DrawArcShape(ArcRenderable r)
        => _shapeBatch.DrawArc(r.Center, r.Angle1, r.Angle2, r.Radius1, r.Radius2, r.Fill, r.Border, r.Thickness);

    internal void DrawRingShape(RingRenderable r)
        => _shapeBatch.DrawRing(r.Center, r.Angle1, r.Angle2, r.Radius1, r.Radius2, r.Fill, r.Border, r.Thickness);
}
