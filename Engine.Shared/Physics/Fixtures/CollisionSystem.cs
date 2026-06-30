using Engine.Shared.Configuration;
using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.Physics.Configuration;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Shared.Physics.Fixtures;

/// <summary>
/// Detects overlaps between CollisionComponents, computes the MTV (Minimum Translation Vector)
/// and fires CollisionStartEvent / CollisionEndEvent.
/// </summary>
public sealed class CollisionSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
 
    // tracks active collision pairs from the previous frame
    private readonly HashSet<(EntityUid, string, EntityUid, string)> _activePairs = new();
 
    // reused every frame to avoid per-Update gc allocations
    internal readonly List<(EntityUid Uid, TransformComponent Transform, CollisionComponent Collision)> _entityBuffer = new();
    private readonly HashSet<(EntityUid, string, EntityUid, string)> _currentPairs = new();

    private int CellSize = 64;
    private readonly Dictionary<(int, int), List<int>> _spatialHash = new();
    private readonly HashSet<long> _testedPairs = new();

    public override void Init()
    {
        base.Init();
        _cfg.Subs(PhysicsCvars.CellSize, v => CellSize = v);
    }
 
    public override void Update(float dt)
    {
        base.Update(dt);
        _entityBuffer.Clear();
        _currentPairs.Clear();
        _testedPairs.Clear();

        foreach (var (uid, col) in GetEntitiesWithComp<CollisionComponent>())
        {
            if (!col.Active) continue;
            if (!TryComp<TransformComponent>(uid, out var transform)) continue;
            _entityBuffer.Add((uid, transform, col));
        }

        foreach (var list in _spatialHash.Values) list.Clear();

        for (int i = 0; i < _entityBuffer.Count; i++)
        {
            var (_, transform, col) = _entityBuffer[i];

            float eMinX =  float.MaxValue, eMinY =  float.MaxValue;
            float eMaxX = -float.MaxValue, eMaxY = -float.MaxValue;

            foreach (var (_, fixture) in col.Fixtures)
            {
                fixture.Shape.GetAABB(transform.Position,
                    out float fx0, out float fy0, out float fx1, out float fy1);
                if (fx0 < eMinX) eMinX = fx0;
                if (fy0 < eMinY) eMinY = fy0;
                if (fx1 > eMaxX) eMaxX = fx1;
                if (fy1 > eMaxY) eMaxY = fy1;
            }

            int cx0 = (int)MathF.Floor(eMinX / CellSize);
            int cy0 = (int)MathF.Floor(eMinY / CellSize);
            int cx1 = (int)MathF.Floor(eMaxX / CellSize);
            int cy1 = (int)MathF.Floor(eMaxY / CellSize);

            for (int cx = cx0; cx <= cx1; cx++)
            for (int cy = cy0; cy <= cy1; cy++)
            {
                var cell = (cx, cy);
                if (!_spatialHash.TryGetValue(cell, out var bucket))
                {
                    bucket = new List<int>(4);
                    _spatialHash[cell] = bucket;
                }
                bucket.Add(i);
            }
        }

        foreach (var bucket in _spatialHash.Values)
        {
            if (bucket.Count < 2) continue;

            for (int i = 0; i < bucket.Count; i++)
            for (int j = i + 1; j < bucket.Count; j++)
            {
                int a = bucket[i], b = bucket[j];
                long pairKey = a < b
                    ? ((long)a << 32) | (uint)b
                    : ((long)b << 32) | (uint)a;

                if (!_testedPairs.Add(pairKey))
                    continue;

                var (uidA, transformA, colA) = _entityBuffer[a];
                var (uidB, transformB, colB) = _entityBuffer[b];

                CheckEntityPair(
                    uidA, transformA.Position, colA,
                    uidB, transformB.Position, colB);
            }
        }

        // fire CollisionEnd for any pairs that are no longer overlapping
        foreach (var pair in _activePairs)
        {
            if (_currentPairs.Contains(pair))
                continue;

            var (uidA, fixA, uidB, fixB) = pair;
            RaiseEvent(uidA, new CollisionEndEvent(uidB, fixA, fixB));
            RaiseEvent(uidB, new CollisionEndEvent(uidA, fixB, fixA));
        }

        _activePairs.Clear();
        foreach (var p in _currentPairs)
            _activePairs.Add(p);
    }

    #region Collision
    
    /// <summary>
    /// Returns true if the entity A and entity B are currently overlapping on any fixture
    /// </summary>
    public bool IsColliding(EntityUid uidA, EntityUid uidB)
    {
        foreach (var (a, _, b, _) in _activePairs)
        {
            if ((a == uidA && b == uidB) || (a == uidB && b == uidA))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the entity A is colliding with any entity on the given fixture.
    /// </summary>
    public bool IsCollidingOnFixture(EntityUid uidA, string fixtureId)
    {
        foreach (var (a, fixA, b, fixB) in _activePairs)
        {
            if (a == uidA && fixA == fixtureId) return true;
            if (b == uidA && fixB == fixtureId) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the entity is colliding with anything at all
    /// </summary>
    public bool IsCollidingWithAnything(EntityUid uid)
    {
        foreach (var (a, _, b, _) in _activePairs)
        {
            if (a == uid || b == uid)
                return true;
        }
        return false;
    }

    /// <summary>
    /// returns all entities currently colliding with uid
    /// </summary>
    public void GetColliding(EntityUid uid, List<EntityUid> results)
    {
        results.Clear();
        foreach (var (a, _, b, _) in _activePairs)
        {
            if (a == uid) results.Add(b);
            else if (b == uid) results.Add(a);
        }
    }
 
    #endregion
    #region Position getters

    /// <summary>
    /// Returns the closest entity whose fixtures contain <paramref name="worldPos"/>.
    /// <returns><see cref="EntityUid.Empty"/> if none found.</returns>
    public EntityUid GetEntityAtPosition(Vector2 worldPos, HashSet<string>? mask = null)
    {
        TryGetEntityAtPosition(worldPos, out var uid, mask);
        return uid;
    }

    /// <summary>
    /// Tries to find the closest entity at the given position.
    /// </summary>
    public bool TryGetEntityAtPosition(Vector2 worldPos, out EntityUid uid, HashSet<string>? mask = null)
    {
        uid = EntityUid.Empty;

        var entities = GetEntitiesAtPosition(worldPos, mask);

        float bestDistSq = float.MaxValue;

        foreach (var entUid in entities)
        {
            if (!TryComp<TransformComponent>(entUid, out var transform))
                continue;

            float distSq = Vector2.DistanceSquared(transform.Position, worldPos);

            if (distSq >= bestDistSq)
                continue;

            bestDistSq = distSq;
            uid = entUid;
        }

        return uid != EntityUid.Empty;
    }

    /// <summary>
    /// Returns all entities whose collision fixtures contain <paramref name="worldPos"/>.
    /// Only entities with an active <see cref="CollisionComponent"/> are considered.
    /// </summary>
    public List<EntityUid> GetEntitiesAtPosition(Vector2 worldPos, HashSet<string>? mask = null)
    {
        var result = new List<EntityUid>();

        foreach (var (entUid, transform) in GetEntitiesWithComp<TransformComponent>())
        {
            if (!TryComp<CollisionComponent>(entUid, out var collision) || !collision.Active)
                continue;

            bool hit = false;

            foreach (var (_, fixture) in collision.Fixtures)
            {
                if (mask != null && !mask.Overlaps(fixture.Layers))
                    continue;

                if (!IsPosInsideFixture(worldPos, transform.Position, fixture.Shape))
                    continue;

                hit = true;
                break;
            }

            if (hit)
                result.Add(entUid);
        }

        return result;
    }

    /// <summary>
    /// Tests whether a world-space position is inside any fixture of a <see cref="CollisionComponent"/>.
    /// </summary>
    public static bool IsPosInsideFixture(Vector2 position, Vector2 entityPos, CollisionShape shape)
    {
        return shape switch
        {
            BoxShape box =>
                box.GetBounds(entityPos).Contains((int)position.X, (int)position.Y),

            CircleShape circle =>
                Vector2.DistanceSquared(position, circle.GetCenter(entityPos))
                <= circle.Radius * circle.Radius,

            PolygonShape polygon =>
                CollisionMath.PointInPolygon(position, polygon.GetWorldVertices(entityPos)),

            _ => false
        };
    }

    #endregion
    #region Raycast
 
    /// <summary>
    /// Casts a ray and returns the closest entity hit.
    /// </summary>
    public bool Raycast(
        Vector2 origin,
        Vector2 direction,
        float maxDistance,
        out RaycastHit hit,
        HashSet<string>? mask = null)
    {
        hit = default;
        float closestDist = maxDistance;
        bool  found       = false;
 
        foreach (var (uid, transform, col) in _entityBuffer)
        {
            foreach (var (id, fixture) in col.Fixtures)
            {
                if (mask != null && !mask.Overlaps(fixture.Layers))
                    continue;
 
                if (!RayVsShape(origin, direction, maxDistance, transform.Position, fixture.Shape, out float dist))
                    continue;
 
                if (dist >= closestDist)
                    continue;
 
                closestDist = dist;
                hit = new RaycastHit
                {
                    Entity    = uid,
                    FixtureId = id,
                    Distance  = dist,
                    Point     = origin + direction * dist,
                };
                found = true;
            }
        }
 
        return found;
    }
 
    /// <summary>
    /// Returns all entities hit by the ray, sorted by distance.
    /// Useful for piercing projectiles or line-of-sight checks.
    /// </summary>
    public List<RaycastHit> RaycastAll(
        Vector2 origin,
        Vector2 direction,
        float maxDistance,
        HashSet<string>? mask = null)
    {
        var hits = new List<RaycastHit>();
 
        foreach (var (uid, transform, col) in _entityBuffer)
        {
            foreach (var (id, fixture) in col.Fixtures)
            {
                if (mask != null && !mask.Overlaps(fixture.Layers))
                    continue;
 
                if (!RayVsShape(origin, direction, maxDistance, transform.Position, fixture.Shape, out float dist))
                    continue;
 
                hits.Add(new RaycastHit
                {
                    Entity    = uid,
                    FixtureId = id,
                    Distance  = dist,
                    Point     = origin + direction * dist,
                });
            }
        }
 
        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return hits;
    }

    #endregion
 
    private void CheckEntityPair(
    EntityUid uidA, Vector2 posA, CollisionComponent colA,
    EntityUid uidB, Vector2 posB, CollisionComponent colB)
    {
        foreach (var (idA, fixA) in colA.Fixtures)
        {
            fixA.Shape.GetAABB(posA, out float axMin, out float ayMin, out float axMax, out float ayMax);
            foreach (var (idB, fixB) in colB.Fixtures)
            {
                bool aSeesB = fixA.Masks.Overlaps(fixB.Layers);
                bool bSeesA = fixB.Masks.Overlaps(fixA.Layers);
                if (!aSeesB && !bSeesA)
                    continue;

                fixB.Shape.GetAABB(posB, out float bxMin, out float byMin, out float bxMax, out float byMax);
                if (axMax < bxMin || axMin > bxMax || ayMax < byMin || ayMin > byMax)
                    continue;

                // TryComputeMTV returns false if no overlap - also gives us the push vector for free
                if (!CollisionMath.TryComputeMTV(fixA.Shape, posA, fixB.Shape, posB, out var mtv))
                    continue;

                var pair = MakePair(uidA, idA, uidB, idB);
                _currentPairs.Add(pair);

                if (fixA.Hard && fixB.Hard)
                {
                    if (!_activePairs.Contains(pair))
                    {
                        // First frame of overlap
                        RaiseEvent(uidA, new CollisionStartEvent(uidB, idA, idB,  mtv, true));
                        RaiseEvent(uidB, new CollisionStartEvent(uidA, idB, idA, -mtv, true));
                    }

                    // Every frame while overlapping — this is what actually resolves
                    //RaiseEvent(uidA, new CollisionStayEvent(uidB, idA, idB,  mtv));
                    //RaiseEvent(uidB, new CollisionStayEvent(uidA, idB, idA, -mtv));
                }
                else
                {
                    if (!_activePairs.Contains(pair))
                    {
                        RaiseEvent(uidA, new CollisionStartEvent(uidB, idA, idB, Vector2.Zero, false));
                        RaiseEvent(uidB, new CollisionStartEvent(uidA, idB, idA, Vector2.Zero, false));
                    }
                }
            }
        }
    }
 
    private static bool RayVsShape(
        Vector2 origin, Vector2 dir, float maxDist,
        Vector2 shapePos, CollisionShape shape, out float dist)
            => shape switch
            {
                BoxShape     box    => RayVsBox(origin, dir, maxDist, box, shapePos, out dist),
                CircleShape  circle => RayVsCircle(origin, dir, maxDist, circle, shapePos, out dist),
                PolygonShape poly   => RayVsPolygon(origin, dir, maxDist, poly, shapePos, out dist),
                _                   => (dist = 0) != 0
            };
 
    private static bool RayVsBox(
        Vector2 origin, Vector2 dir, float maxDist,
        BoxShape box, Vector2 pos, out float dist)
    {
        dist = 0;
        var b = box.GetBounds(pos);
 
        // Guard against division by zero for axis-aligned rays
        float tMinX = dir.X != 0f ? (b.Left   - origin.X) / dir.X : float.NegativeInfinity;
        float tMaxX = dir.X != 0f ? (b.Right  - origin.X) / dir.X : float.PositiveInfinity;
        float tMinY = dir.Y != 0f ? (b.Top    - origin.Y) / dir.Y : float.NegativeInfinity;
        float tMaxY = dir.Y != 0f ? (b.Bottom - origin.Y) / dir.Y : float.PositiveInfinity;
 
        if (tMinX > tMaxX) (tMinX, tMaxX) = (tMaxX, tMinX);
        if (tMinY > tMaxY) (tMinY, tMaxY) = (tMaxY, tMinY);
 
        float tEnter = MathF.Max(tMinX, tMinY);
        float tExit  = MathF.Min(tMaxX, tMaxY);
 
        if (tExit < 0f || tEnter > tExit || tEnter > maxDist)
            return false;
 
        dist = MathF.Max(tEnter, 0f);
        return true;
    }
 
    private static bool RayVsCircle(
        Vector2 origin, Vector2 dir, float maxDist,
        CircleShape circle, Vector2 pos, out float dist)
    {
        dist = 0;
        var   center       = circle.GetCenter(pos);
        var   oc           = origin - center;
        float b            = Vector2.Dot(oc, dir);
        float c            = Vector2.Dot(oc, oc) - circle.Radius * circle.Radius;
        float discriminant = b * b - c;
 
        if (discriminant < 0f)
            return false;
 
        float sqrtDisc = MathF.Sqrt(discriminant);
        float t        = -b - sqrtDisc;
 
        if (t < 0f) t = -b + sqrtDisc;
        if (t < 0f || t > maxDist)
            return false;
 
        dist = t;
        return true;
    }
 
    private static bool RayVsPolygon(
        Vector2 origin, Vector2 dir, float maxDist,
        PolygonShape poly, Vector2 pos, out float dist)
    {
        dist = float.MaxValue;
        bool hit   = false;
        var  verts = poly.GetWorldVertices(pos);
 
        // Ray origin inside polygon counts as a hit at t=0
        if (CollisionMath.PointInPolygon(origin, verts))
        {
            dist = 0f;
            return true;
        }
 
        for (int i = 0; i < verts.Length; i++)
        {
            var v0 = verts[i];
            var v1 = verts[(i + 1) % verts.Length];
 
            if (!RayVsSegment(origin, dir, v0, v1, out float t))
                continue;
 
            if (t < 0f || t > maxDist)
                continue;
 
            if (t < dist)
            {
                dist = t;
                hit  = true;
            }
        }
 
        if (!hit) dist = 0f;
        return hit;
    }
 
    /// <summary>
    /// Parametric ray vs line segment intersection.
    /// Returns t along the ray at the intersection point.
    /// s is the position on the segment [0, 1].
    /// </summary>
    private static bool RayVsSegment(Vector2 origin, Vector2 dir, Vector2 v0, Vector2 v1, out float t)
    {
        t = 0f;
        var   edge     = v1 - v0;
        var   toOrigin = origin - v0;
        float denom    = dir.X * edge.Y - dir.Y * edge.X;
 
        if (MathF.Abs(denom) < 1e-10f)
            return false; // parallel
 
        float s = (toOrigin.X * dir.Y  - toOrigin.Y * dir.X)  / denom;
        t       = (toOrigin.X * edge.Y - toOrigin.Y * edge.X) / denom;
 
        return s >= 0f && s <= 1f && t >= 0f;
    }

    public bool TryGetPenetration(EntityUid uidA, EntityUid uidB, out Vector2 mtv)
    {
        mtv = Vector2.Zero;

        if (!TryComp<TransformComponent>(uidA, out var tA) ||
            !TryComp<CollisionComponent>(uidA, out var cA) ||
            !TryComp<TransformComponent>(uidB, out var tB) ||
            !TryComp<CollisionComponent>(uidB, out var cB))
            return false;

        foreach (var (_, fixA) in cA.Fixtures)
        foreach (var (_, fixB) in cB.Fixtures)
        {
            if (CollisionMath.TryComputeMTV(fixA.Shape, tA.Position, fixB.Shape, tB.Position, out mtv))
                return true;
        }

        return false;
    }
 
    /// <summary>
    /// Returns a canonical pair key so that (A,B) and (B,A)
    /// always map to the same entry in the hash set.
    /// </summary>
    private static (EntityUid, string, EntityUid, string) MakePair(
        EntityUid a, string fixA, EntityUid b, string fixB)
            => a.Id < b.Id ? (a, fixA, b, fixB) : (b, fixB, a, fixA);
}
 
public struct RaycastHit
{
    public EntityUid Entity;
    public string    FixtureId;
    public float     Distance;
    public Vector2   Point;
}