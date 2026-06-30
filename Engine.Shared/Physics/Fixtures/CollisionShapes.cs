//using Engine.Client.Graphics;
using Microsoft.Xna.Framework;
using System;

namespace Engine.Shared.Physics.Fixtures;

public abstract class CollisionShape
{
    public Vector2 Offset { get; set; } = Vector2.Zero;

    public abstract bool Intersects(CollisionShape other, Vector2 selfPos, Vector2 otherPos);
    
    //? DebugDrawing has been passed to the CollisionDrawSystem
    //public abstract void DebugDraw(Vector2 worldPos, RenderManager render);

    /// <summary>
    /// Returns an axis-aligned bounding box in world space.
    /// Used as a cheap broad-phase rejection before running narrow-phase tests.
    /// </summary>
    public abstract void GetAABB(Vector2 worldPos,
        out float minX, out float minY, out float maxX, out float maxY);
}

public sealed class BoxShape : CollisionShape
{
    public Vector2 Size { get; set; } = Vector2.One;

    public Rectangle GetBounds(Vector2 worldPos)
    {
        var pos = worldPos + Offset;
        return new Rectangle(
            (int)(pos.X - Size.X / 2f),
            (int)(pos.Y - Size.Y / 2f),
            (int)Size.X,
            (int)Size.Y
        );
    }

    public override void GetAABB(Vector2 worldPos,
        out float minX, out float minY, out float maxX, out float maxY)
    {
        var center = worldPos + Offset;
        float halfW = Size.X * 0.5f;
        float halfH = Size.Y * 0.5f;
        minX = center.X - halfW;
        minY = center.Y - halfH;
        maxX = center.X + halfW;
        maxY = center.Y + halfH;
    }

    public override bool Intersects(CollisionShape other, Vector2 selfPos, Vector2 otherPos)
        => other switch
        {
            BoxShape     box    => GetBounds(selfPos).Intersects(box.GetBounds(otherPos)),
            CircleShape  circle => CollisionMath.BoxVsCircle(this, selfPos, circle, otherPos),
            PolygonShape poly   => CollisionMath.PolygonVsBox(poly, otherPos, this, selfPos),
            _                   => false
        };

    // public override void DebugDraw(Vector2 worldPos, RenderManager render)
    // {
    //     render.DrawRect(GetBounds(worldPos), Color.Lime);
    // }
}

public sealed class CircleShape : CollisionShape
{
    public float Radius { get; set; } = 1f;

    public Vector2 GetCenter(Vector2 worldPos) => worldPos + Offset;

    public override void GetAABB(Vector2 worldPos,
        out float minX, out float minY, out float maxX, out float maxY)
    {
        var center = GetCenter(worldPos);
        minX = center.X - Radius;
        minY = center.Y - Radius;
        maxX = center.X + Radius;
        maxY = center.Y + Radius;
    }

    public override bool Intersects(CollisionShape other, Vector2 selfPos, Vector2 otherPos)
        => other switch
        {
            CircleShape  circle => CollisionMath.CircleVsCircle(this, selfPos, circle, otherPos),
            BoxShape     box    => CollisionMath.BoxVsCircle(box, otherPos, this, selfPos),
            PolygonShape poly   => CollisionMath.PolygonVsCircle(poly, otherPos, this, selfPos),
            _                   => false
        };

    // public override void DebugDraw(Vector2 worldPos, RenderManager render)
    // {
    //     render.DrawCircle(GetCenter(worldPos), Radius, Color.Cyan);
    // }
}

public sealed class PolygonShape : CollisionShape
{
    public Vector2[] Vertices { get; set; } = Array.Empty<Vector2>();

    public Vector2[] GetWorldVertices(Vector2 worldPos)
    {
        var offset = worldPos + Offset;
        var result = new Vector2[Vertices.Length];
        for (int i = 0; i < Vertices.Length; i++)
            result[i] = Vertices[i] + offset;
        return result;
    }

    public override void GetAABB(Vector2 worldPos,
        out float minX, out float minY, out float maxX, out float maxY)
    {
        var offset = worldPos + Offset;
        minX = minY =  float.MaxValue;
        maxX = maxY = -float.MaxValue;
        foreach (var v in Vertices)
        {
            float wx = v.X + offset.X;
            float wy = v.Y + offset.Y;
            if (wx < minX) minX = wx;
            if (wx > maxX) maxX = wx;
            if (wy < minY) minY = wy;
            if (wy > maxY) maxY = wy;
        }
    }

    public override bool Intersects(CollisionShape other, Vector2 selfPos, Vector2 otherPos)
        => other switch
        {
            PolygonShape poly   => CollisionMath.PolygonVsPolygon(this, selfPos, poly, otherPos),
            BoxShape     box    => CollisionMath.PolygonVsBox(this, selfPos, box, otherPos),
            CircleShape  circle => CollisionMath.PolygonVsCircle(this, selfPos, circle, otherPos),
            _                   => false
        };

    // public override void DebugDraw(Vector2 worldPos, RenderManager render)
    // {
    //     render.DrawPolygon(GetWorldVertices(worldPos), Color.Yellow);
    // }
}
