using System;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Physics.Fixtures;

public static class CollisionMath
{
    // ================================
    // Boolean intersection tests
    // (used by CollisionShape.Intersects)
    // ================================
 
    public static bool CircleVsCircle(CircleShape a, Vector2 posA, CircleShape b, Vector2 posB)
        => TryCircleVsCircle(a, posA, b, posB, out _);
 
    public static bool BoxVsCircle(BoxShape box, Vector2 boxPos, CircleShape circle, Vector2 circlePos)
        => TryBoxVsCircle(box, boxPos, circle, circlePos, out _);
 
    public static bool PolygonVsPolygon(PolygonShape a, Vector2 posA, PolygonShape b, Vector2 posB)
        => TryPolygonVsPolygon(a, posA, b, posB, out _);
 
    public static bool PolygonVsBox(PolygonShape poly, Vector2 polyPos, BoxShape box, Vector2 boxPos)
        => TryPolygonVsBox(poly, polyPos, box, boxPos, out _);
 
    public static bool PolygonVsCircle(PolygonShape poly, Vector2 polyPos, CircleShape circle, Vector2 circlePos)
        => TryPolygonVsCircle(poly, polyPos, circle, circlePos, out _);
 
    /// <summary>
    /// Returns the MTV that pushes shapeA out of shapeB, or Zero if not implemented for this pair.
    /// The MTV always points from B toward A.
    /// </summary>
    public static bool TryComputeMTV(
        CollisionShape shapeA, Vector2 posA,
        CollisionShape shapeB, Vector2 posB,
        out Vector2 mtv)
    {
        mtv = Vector2.Zero;
        return (shapeA, shapeB) switch
        {
            (BoxShape a,     BoxShape b)     => TryBoxVsBox(a, posA, b, posB, out mtv),
            (BoxShape a,     CircleShape b)  => TryBoxVsCircle(a, posA, b, posB, out mtv),
            (CircleShape a,  BoxShape b)     => Flip(TryBoxVsCircle(b, posB, a, posA, out mtv), ref mtv),
            (CircleShape a,  CircleShape b)  => TryCircleVsCircle(a, posA, b, posB, out mtv),
            (PolygonShape a, PolygonShape b) => TryPolygonVsPolygon(a, posA, b, posB, out mtv),
            (PolygonShape a, BoxShape b)     => TryPolygonVsBox(a, posA, b, posB, out mtv),
            (BoxShape a,     PolygonShape b) => Flip(TryPolygonVsBox(b, posB, a, posA, out mtv), ref mtv),
            (PolygonShape a, CircleShape b)  => TryPolygonVsCircle(a, posA, b, posB, out mtv),
            (CircleShape a,  PolygonShape b) => Flip(TryPolygonVsCircle(b, posB, a, posA, out mtv), ref mtv),
            _                                => false
        };
    }
 
    public static bool TryBoxVsBox(BoxShape a, Vector2 posA, BoxShape b, Vector2 posB, out Vector2 mtv)
    {
        mtv = Vector2.Zero;
        var boundsA = a.GetBounds(posA);
        var boundsB = b.GetBounds(posB);
        var overlap = Rectangle.Intersect(boundsA, boundsB);
 
        if (overlap.IsEmpty)
            return false;
 
        // Use axis of least penetration, direction points from B toward A
        if (overlap.Width < overlap.Height)
            mtv = new Vector2(overlap.Width * (posA.X > posB.X ? 1f : -1f), 0f);
        else
            mtv = new Vector2(0f, overlap.Height * (posA.Y > posB.Y ? 1f : -1f));
 
        return true;
    }
 
    public static bool TryBoxVsCircle(BoxShape box, Vector2 boxPos, CircleShape circle, Vector2 circlePos, out Vector2 mtv)
    {
        mtv = Vector2.Zero;
        var bounds = box.GetBounds(boxPos);
        var center = circle.GetCenter(circlePos);
 
        float closestX = Math.Clamp(center.X, bounds.Left, bounds.Right);
        float closestY = Math.Clamp(center.Y, bounds.Top,  bounds.Bottom);
        var   closest  = new Vector2(closestX, closestY);
 
        bool centerInsideBox = bounds.Contains((int)center.X, (int)center.Y);
 
        if (centerInsideBox)
        {
            // Circle center is inside the box — find which face is closest to push out from
            float dLeft   = center.X - bounds.Left;
            float dRight  = bounds.Right  - center.X;
            float dTop    = center.Y - bounds.Top;
            float dBottom = bounds.Bottom - center.Y;
 
            float minDist = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));
 
            if (minDist == dLeft)   mtv = new Vector2(-(dLeft   + circle.Radius), 0f);
            else if (minDist == dRight)  mtv = new Vector2(dRight  + circle.Radius, 0f);
            else if (minDist == dTop)    mtv = new Vector2(0f, -(dTop    + circle.Radius));
            else                         mtv = new Vector2(0f, dBottom + circle.Radius);
 
            return true;
        }
 
        var   delta    = center - closest;
        float distSq   = delta.LengthSquared();
        float radiusSq = circle.Radius * circle.Radius;
 
        if (distSq > radiusSq)
            return false;
 
        float dist  = MathF.Sqrt(distSq);
        float depth = circle.Radius - dist;
 
        // Direction pushes box away from circle (box is A, circle is B)
        var dir = dist > 0f
            ? -delta / dist         // from circle center toward closest point = away from circle
            : new Vector2(1f, 0f);  // degenerate case
 
        mtv = dir * depth;
        return true;
    }
 
    public static bool TryCircleVsCircle(CircleShape a, Vector2 posA, CircleShape b, Vector2 posB, out Vector2 mtv)
    {
        mtv = Vector2.Zero;
        var centerA = a.GetCenter(posA);
        var centerB = b.GetCenter(posB);
        var delta   = centerA - centerB;
        float distSq    = delta.LengthSquared();
        float radiusSum = a.Radius + b.Radius;
 
        if (distSq > radiusSum * radiusSum)
            return false;
 
        float dist  = MathF.Sqrt(distSq);
        float depth = radiusSum - dist;
 
        // Direction from B toward A
        var dir = dist > 0f
            ? delta / dist
            : new Vector2(0f, 1f); // identical positions
 
        mtv = dir * depth;
        return true;
    }
 
    public static bool TryPolygonVsPolygon(PolygonShape a, Vector2 posA, PolygonShape b, Vector2 posB, out Vector2 mtv)
    {
        mtv = Vector2.Zero;
        var vertsA = a.GetWorldVertices(posA);
        var vertsB = b.GetWorldVertices(posB);
 
        float   minDepth = float.MaxValue;
        Vector2 minAxis  = Vector2.Zero;
 
        if (!SATFindMTV(vertsA, vertsB, ref minDepth, ref minAxis)) return false;
        if (!SATFindMTV(vertsB, vertsA, ref minDepth, ref minAxis)) return false;
 
        // Ensure MTV points from B toward A
        var centerA = Centroid(vertsA);
        var centerB = Centroid(vertsB);
        if (Vector2.Dot(minAxis, centerA - centerB) < 0f)
            minAxis = -minAxis;
 
        mtv = minAxis * minDepth;
        return true;
    }
 
    public static bool TryPolygonVsBox(PolygonShape poly, Vector2 polyPos, BoxShape box, Vector2 boxPos, out Vector2 mtv)
    {
        var boxPoly = BoxToPolygon(box, boxPos);
        return TryPolygonVsPolygon(poly, polyPos, boxPoly, Vector2.Zero, out mtv);
    }
 
    public static bool TryPolygonVsCircle(PolygonShape poly, Vector2 polyPos, CircleShape circle, Vector2 circlePos, out Vector2 mtv)
    {
        mtv = Vector2.Zero;
        var verts  = poly.GetWorldVertices(polyPos);
        var center = circle.GetCenter(circlePos);
 
        float   minDepth = float.MaxValue;
        Vector2 minAxis  = Vector2.Zero;
 
        // Test polygon edge normals
        for (int i = 0; i < verts.Length; i++)
        {
            var edge   = verts[(i + 1) % verts.Length] - verts[i];
            var axis   = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
 
            ProjectPolygon(verts, axis, out float minA, out float maxA);
            float centerProj = Vector2.Dot(center, axis);
            float minB = centerProj - circle.Radius;
            float maxB = centerProj + circle.Radius;
 
            float overlap = GetOverlap(minA, maxA, minB, maxB);
            if (overlap <= 0f) return false;
 
            if (overlap < minDepth)
            {
                minDepth = overlap;
                minAxis  = axis;
            }
        }
 
        // Test axis from polygon center to circle center (important for circle inside polygon)
        var polyCenter  = Centroid(verts);
        var circleAxis  = Vector2.Normalize(center - polyCenter);
        ProjectPolygon(verts, circleAxis, out float pMin, out float pMax);
        float cProj   = Vector2.Dot(center, circleAxis);
        float overlap2 = GetOverlap(pMin, pMax, cProj - circle.Radius, cProj + circle.Radius);
        if (overlap2 <= 0f) return false;
        if (overlap2 < minDepth)
        {
            minDepth = overlap2;
            minAxis  = circleAxis;
        }
 
        // Ensure MTV points from circle (B) toward polygon (A)
        if (Vector2.Dot(minAxis, polyCenter - center) < 0f)
            minAxis = -minAxis;
 
        mtv = minAxis * minDepth;
        return true;
    }

    private static bool SATFindMTV(
        Vector2[] vertsA, Vector2[] vertsB,
        ref float minDepth, ref Vector2 minAxis)
    {
        for (int i = 0; i < vertsA.Length; i++)
        {
            var edge = vertsA[(i + 1) % vertsA.Length] - vertsA[i];
            var axis = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
 
            ProjectPolygon(vertsA, axis, out float minA, out float maxA);
            ProjectPolygon(vertsB, axis, out float minB, out float maxB);
 
            float overlap = GetOverlap(minA, maxA, minB, maxB);
            if (overlap <= 0f) return false;
 
            if (overlap < minDepth)
            {
                minDepth = overlap;
                minAxis  = axis;
            }
        }
        return true;
    }
 
    private static void ProjectPolygon(Vector2[] verts, Vector2 axis, out float min, out float max)
    {
        min = max = Vector2.Dot(verts[0], axis);
        for (int i = 1; i < verts.Length; i++)
        {
            float p = Vector2.Dot(verts[i], axis);
            if (p < min) min = p;
            if (p > max) max = p;
        }
    }
 
    private static float GetOverlap(float minA, float maxA, float minB, float maxB)
        => MathF.Min(maxA, maxB) - MathF.Max(minA, minB);
 
    private static Vector2 Centroid(Vector2[] verts)
    {
        var sum = Vector2.Zero;
        foreach (var v in verts) sum += v;
        return sum / verts.Length;
    }

    private static readonly Vector2[]    _boxPolyVerts = new Vector2[4];
    private static readonly PolygonShape _boxPolyCache = new() { Vertices = _boxPolyVerts };
    private static PolygonShape BoxToPolygon(BoxShape box, Vector2 pos)
    {
        var b = box.GetBounds(pos);
        _boxPolyVerts[0] = new Vector2(b.Left,  b.Top);
        _boxPolyVerts[1] = new Vector2(b.Right, b.Top);
        _boxPolyVerts[2] = new Vector2(b.Right, b.Bottom);
        _boxPolyVerts[3] = new Vector2(b.Left,  b.Bottom);
        return _boxPolyCache;
    }
 
    public static bool PointInPolygon(Vector2 point, Vector2[] verts)
    {
        bool inside = false;
        for (int i = 0, j = verts.Length - 1; i < verts.Length; j = i++)
        {
            if ((verts[i].Y > point.Y) != (verts[j].Y > point.Y) &&
                point.X < (verts[j].X - verts[i].X) * (point.Y - verts[i].Y)
                        / (verts[j].Y - verts[i].Y) + verts[i].X)
                inside = !inside;
        }
        return inside;
    }
 
    private static bool Flip(bool result, ref Vector2 mtv)
    {
        mtv = -mtv;
        return result;
    }
}
