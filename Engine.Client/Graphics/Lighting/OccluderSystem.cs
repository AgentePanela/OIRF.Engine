using System;
using System.Collections.Generic;
using Engine.Shared.Common;
using Engine.Shared.Configuration;
using Engine.Shared.Containers;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.Graphics;
using Engine.Shared.Lighting;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

internal readonly record struct ShadowSegment(Vector2 A, Vector2 B, Vector3 Transmit = default);

/// <summary>
/// Controls where shadows can cast.
/// </summary>
public sealed class OccluderSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly IRenderManager _renderMan = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    // the world is split in cells this big to find the occluders near a light
    private static readonly float CellSize = 256f;

    // edges are matched to their twin on the occluder next door with their ends rounded to this
    private static readonly float EdgeQuantum = 1f / 16f;

    // longer edges are cut in pieces
    private readonly float MaxSegmentLength = 64f;

    private readonly int CircleSides = 12;

    private sealed class Entry
    {
        public Vector2[] Points = [];

        /// <summary>
        /// Points away from the occluder, one per edge (from Points[i] to Points[i + 1]).
        /// </summary>
        public Vector2[] Normals = [];

        public RectangleF Bounds;

        /// <summary>
        /// The light that goes through it. Zero for an opaque one.
        /// </summary>
        public Vector3 Transmit;

        /// <summary>
        /// Light goes through
        /// </summary>
        public bool SeeThrough => Transmit != Vector3.Zero;

        public readonly List<(int X, int Y)> Cells = new();

        // the last query that went through it, so an occluder in several cells is looked at once
        public int Stamp;
    }

    private readonly record struct EdgeKey(int X0, int Y0, int X1, int Y1);

    private readonly Dictionary<EntityUid, Entry> _entries = new();
    private readonly Dictionary<(int X, int Y), List<Entry>> _cells = new();

    private readonly Dictionary<EdgeKey, int> _edgeCounts = new();

    private readonly Dictionary<(EdgeKey Edge, Vector3 Transmit), int> _seeThroughEdgeCounts = new();

    // goes up whenever an outline is added or taken away, which changes which edges are shared
    private int _outlinesVersion;

    /// <summary>
    /// Goes up whenever an outline is added or taken away.
    /// </summary>
    internal int OutlinesVersion
    {
        get
        {
            Update();
            return _outlinesVersion;
        }
    }

    private readonly HashSet<EntityUid> _dirty = new();

    private int _stamp;

    // where an outline appeared, went away or changed since the last TakeChanges
    private readonly List<RectangleF> _changes = new();

    private OccluderDebugOverlay? _debug;

    public override void Init()
    {
        base.Init();

        SubscribeEvent<OccluderComponent, CompAddedEvent>(OnAdded);
        SubscribeEvent<OccluderComponent, CompRemovedEvent>(OnRemoved);
        SubscribeEvent<OccluderComponent, MoveEvent>(OnMoved);
        SubscribeEvent<OccluderComponent, EntGotInsertedIntoContainerEvent>(OnInserted);
        SubscribeEvent<OccluderComponent, EntGotRemovedFromContainerEvent>(OnTakenOut);
        SubscribeEvent<OccluderComponent, ComponentStateAppliedEvent>(OnStateApplied);

        _debug = new OccluderDebugOverlay(this);
        _renderMan.AddOverlay(_debug);
        _cfg.Subs(LightingCvars.DebugOccluders, v => _debug.Enabled = v);
    }

    public override void OnShutdown()
    {
        base.OnShutdown();

        if (_debug is not null)
            _renderMan.RemoveOverlay(_debug);
    }

    private void OnAdded(EntityUid uid, OccluderComponent comp, CompAddedEvent args)
        => _dirty.Add(uid);

    private void OnRemoved(EntityUid uid, OccluderComponent comp, CompRemovedEvent args)
        => Forget(uid);

    private void OnMoved(EntityUid uid, OccluderComponent comp, MoveEvent args)
        => _dirty.Add(uid);

    // a contained occluder throws no shadow
    private void OnInserted(EntityUid uid, OccluderComponent comp, EntGotInsertedIntoContainerEvent args)
        => _dirty.Add(uid);

    private void OnTakenOut(EntityUid uid, OccluderComponent comp, EntGotRemovedFromContainerEvent args)
        => _dirty.Add(uid);

    private void OnStateApplied(EntityUid uid, OccluderComponent comp, ComponentStateAppliedEvent args)
    {
        if (args.Component is OccluderComponent)
            _dirty.Add(uid);
    }

    /// <summary>
    /// Makes the outline of an occluder again on the next query, for a change no event tells about.
    /// </summary>
    public void Refresh(EntityUid uid)
        => _dirty.Add(uid);

    /// <summary>
    /// Moves to <paramref name="areas"/> every area where the shadows changed since the last call
    /// </summary>
    internal void TakeChanges(List<RectangleF> areas)
    {
        Update();
        areas.AddRange(_changes);
        _changes.Clear();
    }

    private void NoteChange(RectangleF bounds)
        => _changes.Add(new RectangleF(bounds.X - 1f, bounds.Y - 1f, bounds.Width + 2f, bounds.Height + 2f));

    /// <summary>
    /// Adds to <paramref name="segments"/> the edges that throw a shadow from a light at <paramref name="light"/>
    /// </summary>
    internal void CollectSegments(Vector2 light, float radius, List<ShadowSegment> segments)
        => Collect(light, radius, segments, seeThrough: false);

    /// <summary>
    /// Adds to <paramref name="segments"/> the edges of the see-through occluders that tint the light at
    /// <paramref name="light"/>.
    /// </summary>
    internal void CollectSeeThroughSegments(Vector2 light, float radius, List<ShadowSegment> segments)
        => Collect(light, radius, segments, seeThrough: true);

    private void Collect(Vector2 light, float radius, List<ShadowSegment> segments, bool seeThrough)
    {
        Update();

        var reach = new RectangleF(light.X - radius, light.Y - radius, radius * 2f, radius * 2f);
        _stamp++;

        var (minX, minY) = ToCell(new Vector2(reach.Left, reach.Top));
        var (maxX, maxY) = ToCell(new Vector2(reach.Right, reach.Bottom));

        for (var cx = minX; cx <= maxX; cx++)
        for (var cy = minY; cy <= maxY; cy++)
        {
            if (!_cells.TryGetValue((cx, cy), out var cell))
                continue;

            foreach (var entry in cell)
            {
                if (entry.Stamp == _stamp)
                    continue;

                entry.Stamp = _stamp;

                if (entry.SeeThrough != seeThrough || !Intersects(entry.Bounds, reach) || Contains(entry, light))
                    continue;

                AddEdges(entry, light, radius, segments);
            }
        }
    }

    private void AddEdges(Entry entry, Vector2 light, float radius, List<ShadowSegment> segments)
    {
        var points = entry.Points;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];

            // facing the light: its shadow is the one of the far side
            if (Vector2.Dot(entry.Normals[i], light - a) >= 0f)
                continue;

            if (IsShared(entry, a, b))
                continue;

            if (DistanceToSegment(light, a, b) > radius)
                continue;

            var length = Vector2.Distance(a, b);
            var pieces = Math.Max(1, (int)MathF.Ceiling(length / MaxSegmentLength));
            for (var p = 0; p < pieces; p++)
                segments.Add(new ShadowSegment(Vector2.Lerp(a, b, p / (float)pieces),
                    Vector2.Lerp(a, b, (p + 1) / (float)pieces), entry.Transmit));
        }
    }

    /// <summary>
    /// Every edge in <paramref name="area"/>, and whether it is the inside of a wall (shared with another occluder).
    /// </summary>
    internal IEnumerable<(Vector2 A, Vector2 B, bool Shared)> EdgesIn(RectangleF area)
    {
        Update();
        _stamp++;

        var (minX, minY) = ToCell(new Vector2(area.Left, area.Top));
        var (maxX, maxY) = ToCell(new Vector2(area.Right, area.Bottom));

        for (var cx = minX; cx <= maxX; cx++)
        for (var cy = minY; cy <= maxY; cy++)
        {
            if (!_cells.TryGetValue((cx, cy), out var cell))
                continue;

            foreach (var entry in cell)
            {
                if (entry.Stamp == _stamp || !Intersects(entry.Bounds, area))
                    continue;

                entry.Stamp = _stamp;
                for (var i = 0; i < entry.Points.Length; i++)
                {
                    var a = entry.Points[i];
                    var b = entry.Points[(i + 1) % entry.Points.Length];
                    yield return (a, b, IsShared(entry, a, b));
                }
            }
        }
    }

    /// <summary>
    /// The lights in <paramref name="area"/> that sit inside an occluder.
    /// </summary>
    internal IEnumerable<Vector2> LightsInsideOccluders(RectangleF area)
    {
        foreach (var (_, light, transform) in GetEntitiesWithComp<PointLightComponent, TransformComponent>())
        {
            var center = transform.Position + light.Offset;
            if (light.Enabled && light.CastShadows
                && center.X >= area.Left && center.X <= area.Right && center.Y >= area.Top && center.Y <= area.Bottom
                && IsInsideOccluder(center))
                yield return center;
        }
    }

    /// <summary>
    /// Whether <paramref name="point"/> is inside an occluder, which keeps a light there from being shadowed by it.
    /// </summary>
    internal bool IsInsideOccluder(Vector2 point)
    {
        Update();

        if (!_cells.TryGetValue(ToCell(point), out var cell))
            return false;

        foreach (var entry in cell)
        {
            if (Contains(entry, point))
                return true;
        }

        return false;
    }

    // makes the outlines of what changed again
    private void Update()
    {
        if (_dirty.Count == 0)
            return;

        foreach (var uid in _dirty)
        {
            Vector2[]? points = null;
            var transmit = Vector3.Zero;
            if (TryComp<OccluderComponent>(uid, out var comp) && comp.Enabled && !HasComp<ContainedComponent>(uid)
                && Transform(uid) is { } transform)
            {
                transmit = comp.Tint.ToVector3() * (1f - Math.Clamp(comp.Opacity, 0f, 1f));
                points = BuildOutline(uid, comp, transform);
                if (points.Length < 3)
                    points = null;
            }

            // the same outline as before changes no shadow, the lights around it can stay as they are
            if (points is not null && _entries.TryGetValue(uid, out var old) && old.Transmit == transmit
                && old.Points.AsSpan().SequenceEqual(points))
                continue;

            Forget(uid);

            if (points is not null)
                Add(uid, points, transmit);
        }

        _dirty.Clear();
    }

    private Vector2[] BuildOutline(EntityUid uid, OccluderComponent comp, TransformComponent transform)
    {
        var angle = transform.Angle;
        var rotation = Matrix.CreateRotationZ(angle);
        var center = transform.Position + (angle == 0f ? comp.Offset : Vector2.Transform(comp.Offset, rotation));

        switch (comp.Shape)
        {
            case OccluderShape.Circle:
            {
                var points = new Vector2[CircleSides];
                for (var i = 0; i < CircleSides; i++)
                {
                    var (sin, cos) = MathF.SinCos(i * MathF.Tau / CircleSides);
                    points[i] = center + new Vector2(cos, sin) * comp.Radius;
                }

                return points;
            }

            case OccluderShape.Sprite:
            {
                if (!TryComp<SpriteComponent>(uid, out var sprite) || _sprites.GetLiveSprite(sprite, transform) is not { } spr)
                    return [];

                // the drawn box of the sprite, around its origin
                var size = new Vector2(spr.Width, spr.Height) * spr.Scale;
                var topLeft = -spr.Origin * spr.Scale;
                return Box(transform.Position + sprite.Offset, topLeft, topLeft + size, rotation, angle);
            }

            default:
            {
                var half = comp.Size / 2f;
                return Box(center, -half, half, rotation, angle);
            }
        }
    }

    private static Vector2[] Box(Vector2 at, Vector2 min, Vector2 max, Matrix rotation, float angle)
    {
        Vector2[] corners = [min, new(max.X, min.Y), max, new(min.X, max.Y)];
        for (var i = 0; i < corners.Length; i++)
            corners[i] = at + (angle == 0f ? corners[i] : Vector2.Transform(corners[i], rotation));

        return corners;
    }

    private void Add(EntityUid uid, Vector2[] points, Vector3 transmit)
    {
        var entry = new Entry
        {
            Points = points,
            Normals = new Vector2[points.Length],
            Transmit = transmit,
        };

        var middle = Vector2.Zero;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var point in points)
        {
            middle += point;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        middle /= points.Length;
        entry.Bounds = new RectangleF(min.X, min.Y, max.X - min.X, max.Y - min.Y);

        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];

            // whichever way it goes around, the normal points away from the middle
            var edge = b - a;
            var normal = Vector2.Normalize(new Vector2(edge.Y, -edge.X));
            if (Vector2.Dot(normal, (a + b) / 2f - middle) < 0f)
                normal = -normal;

            entry.Normals[i] = normal;
            CountEdge(entry, a, b, 1);
        }

        var (minX, minY) = ToCell(min);
        var (maxX, maxY) = ToCell(max);
        for (var cx = minX; cx <= maxX; cx++)
        for (var cy = minY; cy <= maxY; cy++)
        {
            if (!_cells.TryGetValue((cx, cy), out var cell))
                _cells[(cx, cy)] = cell = new List<Entry>();

            cell.Add(entry);
            entry.Cells.Add((cx, cy));
        }

        _entries[uid] = entry;
        _outlinesVersion++;
        NoteChange(entry.Bounds);
    }

    private void Forget(EntityUid uid)
    {
        if (!_entries.Remove(uid, out var entry))
            return;

        _outlinesVersion++;
        NoteChange(entry.Bounds);

        foreach (var coords in entry.Cells)
        {
            if (!_cells.TryGetValue(coords, out var cell))
                continue;

            cell.Remove(entry);
            if (cell.Count == 0)
                _cells.Remove(coords);
        }

        for (var i = 0; i < entry.Points.Length; i++)
            CountEdge(entry, entry.Points[i], entry.Points[(i + 1) % entry.Points.Length], -1);
    }

    private bool IsShared(Entry entry, Vector2 a, Vector2 b)
    {
        var key = KeyOf(a, b);
        var count = entry.SeeThrough
            ? _seeThroughEdgeCounts.GetValueOrDefault((key, entry.Transmit))
            : _edgeCounts.GetValueOrDefault(key);

        return count > 1;
    }

    private void CountEdge(Entry entry, Vector2 a, Vector2 b, int by)
    {
        var key = KeyOf(a, b);
        if (entry.SeeThrough)
            Count(_seeThroughEdgeCounts, (key, entry.Transmit), by);
        else
            Count(_edgeCounts, key, by);
    }

    private static void Count<TKey>(Dictionary<TKey, int> counts, TKey key, int by) where TKey : notnull
    {
        var count = counts.GetValueOrDefault(key) + by;
        if (count <= 0)
            counts.Remove(key);
        else
            counts[key] = count;
    }

    private static (int X, int Y) ToCell(Vector2 position)
        => ((int)MathF.Floor(position.X / CellSize), (int)MathF.Floor(position.Y / CellSize));

    // the same for both directions of an edge
    private static EdgeKey KeyOf(Vector2 a, Vector2 b)
    {
        var (ax, ay) = ((int)MathF.Round(a.X / EdgeQuantum), (int)MathF.Round(a.Y / EdgeQuantum));
        var (bx, by) = ((int)MathF.Round(b.X / EdgeQuantum), (int)MathF.Round(b.Y / EdgeQuantum));

        return ax < bx || (ax == bx && ay <= by) ? new EdgeKey(ax, ay, bx, by) : new EdgeKey(bx, by, ax, ay);
    }

    // the outlines are convex: inside is behind every edge
    private static bool Contains(Entry entry, Vector2 point)
    {
        if (point.X < entry.Bounds.Left || point.X > entry.Bounds.Right
            || point.Y < entry.Bounds.Top || point.Y > entry.Bounds.Bottom)
            return false;

        for (var i = 0; i < entry.Points.Length; i++)
        {
            if (Vector2.Dot(entry.Normals[i], point - entry.Points[i]) > 0f)
                return false;
        }

        return true;
    }

    private static bool Intersects(RectangleF a, RectangleF b)
        => a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var edge = b - a;
        var lengthSquared = edge.LengthSquared();
        var t = lengthSquared <= 0f ? 0f : Math.Clamp(Vector2.Dot(point - a, edge) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + edge * t);
    }
}
