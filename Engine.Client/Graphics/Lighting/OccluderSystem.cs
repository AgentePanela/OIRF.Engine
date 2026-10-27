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

internal readonly record struct ShadowSegment(Vector2 A, Vector2 B);

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
        public float FovHeight;

        /// <summary>
        /// Its outline is a box along the axes, which the field of view raises by <see cref="FovHeight"/>.
        /// </summary>
        public bool IsAxisBox;

        // whether another occluder stands on its north side
        public bool TopShared;
        public int TopSharedVersion = -1;
        public readonly List<(int X, int Y)> Cells = new();

        // the last query that went through it, so an occluder in several cells is looked at once
        public int Stamp;
    }

    private readonly record struct EdgeKey(int X0, int Y0, int X1, int Y1);

    private readonly Dictionary<EntityUid, Entry> _entries = new();
    private readonly Dictionary<(int X, int Y), List<Entry>> _cells = new();

    private readonly Dictionary<EdgeKey, int> _edgeCounts = new();

    // goes up whenever an outline is added or taken away, which changes which edges are shared
    private int _outlinesVersion;

    private readonly HashSet<EntityUid> _dirty = new();

    private float _highFovHeight;
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

                if (!Intersects(entry.Bounds, reach) || Contains(entry, light))
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

            if (_edgeCounts.TryGetValue(KeyOf(a, b), out var count) && count > 1)
                continue;

            if (DistanceToSegment(light, a, b) > radius)
                continue;

            var length = Vector2.Distance(a, b);
            var pieces = Math.Max(1, (int)MathF.Ceiling(length / MaxSegmentLength));
            for (var p = 0; p < pieces; p++)
                segments.Add(new ShadowSegment(Vector2.Lerp(a, b, p / (float)pieces), Vector2.Lerp(a, b, (p + 1) / (float)pieces)));
        }
    }

    internal void CollectFovSegments(Vector2 eye, float radius, List<ShadowSegment> segments)
    {
        Update();
        _lineEndCount = 0;

        // one standing below the reach can hide something in it
        var reach = new RectangleF(eye.X - radius, eye.Y - radius, radius * 2f, radius * 2f + _highFovHeight);
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

                // standing against a wall is not being in it: that wall still hides what is past it
                if (!Intersects(entry.Bounds, reach) || Contains(entry, eye, FovInside))
                    continue;

                if (!AddFovBox(entry, eye))
                    AddEdges(entry, eye, radius, segments);
            }
        }

        // boxes next to each other become one outline: where two of them touch, nothing is cast
        Array.Sort(_lineKeys, _lineEnds, 0, _lineEndCount);
        AddLineEdges(eye, radius, segments);
    }

    // Where a side of a box occluder starts (+1) or ends (-1) on the line it lies on.
    private readonly record struct LineEnd(int Outward, int Inward);

    // The ends of the sides, sorted by their keys
    private long[] _lineKeys = new long[256];
    private LineEnd[] _lineEnds = new LineEnd[256];
    private int _lineEndCount;

    private const int AtBits = 31;
    private const int LineBits = 30;
    private const long AtMask = (1L << AtBits) - 1;
    private const long LineMask = (1L << LineBits) - 1;

    private static long LineKey(bool vertical, int line, int at)
        => ((vertical ? 1L : 0L) << (LineBits + AtBits))
           | (((line + (1L << (LineBits - 1))) & LineMask) << AtBits)
           | ((at + (1L << (AtBits - 1))) & AtMask);

    // all but where along the line
    private static long LineOf(long key)
        => key >> AtBits;

    private static int AtOf(long key)
        => (int)((key & AtMask) - (1L << (AtBits - 1)));

    /// <summary>
    /// An axis aligned box occluder, raised by its FovHeight, split in the lines of its sides. False for any other shape.
    /// </summary>
    /// <param name="entry"></param>
    /// <param name="eye"></param>
    /// <returns></returns>
    private bool AddFovBox(Entry entry, Vector2 eye)
    {
        if (!entry.IsAxisBox)
            return false;

        var bounds = entry.Bounds;
        var top = bounds.Top;

        // Raised, unless another occluder stands on its north side
        var eyeBehind = eye.Y > top - entry.FovHeight && eye.Y <= top;
        if (entry.FovHeight > 0f && !eyeBehind && !IsTopShared(entry))
            top -= entry.FovHeight;

        AddLineEdge(true, bounds.Left, top, bounds.Bottom, -1);
        AddLineEdge(true, bounds.Right, top, bounds.Bottom, 1);
        AddLineEdge(false, top, bounds.Left, bounds.Right, -1);
        AddLineEdge(false, bounds.Bottom, bounds.Left, bounds.Right, 1);
        return true;
    }

    private bool IsTopShared(Entry entry)
    {
        if (entry.TopSharedVersion != _outlinesVersion)
        {
            var bounds = entry.Bounds;
            var key = KeyOf(new Vector2(bounds.Left, bounds.Top), new Vector2(bounds.Right, bounds.Top));
            entry.TopShared = _edgeCounts.TryGetValue(key, out var count) && count > 1;
            entry.TopSharedVersion = _outlinesVersion;
        }

        return entry.TopShared;
    }

    private void AddLineEdge(bool vertical, float at, float from, float to, int side)
    {
        if (_lineEndCount + 2 > _lineKeys.Length)
        {
            Array.Resize(ref _lineKeys, _lineKeys.Length * 2);
            Array.Resize(ref _lineEnds, _lineEnds.Length * 2);
        }

        var line = (int)MathF.Round(at / EdgeQuantum);
        var outward = side > 0 ? 1 : 0;
        var inward = side > 0 ? 0 : 1;

        _lineKeys[_lineEndCount] = LineKey(vertical, line, (int)MathF.Round(from / EdgeQuantum));
        _lineEnds[_lineEndCount++] = new LineEnd(outward, inward);
        _lineKeys[_lineEndCount] = LineKey(vertical, line, (int)MathF.Round(to / EdgeQuantum));
        _lineEnds[_lineEndCount++] = new LineEnd(-outward, -inward);
    }

    private void AddLineEdges(Vector2 eye, float radius, List<ShadowSegment> segments)
    {
        var i = 0;
        while (i < _lineEndCount)
        {
            var line = LineOf(_lineKeys[i]);
            var vertical = line >> LineBits != 0;
            var at = ((line & LineMask) - (1L << (LineBits - 1))) * EdgeQuantum;

            var outward = 0;
            var inward = 0;
            var runSide = 0;
            var runFrom = 0f;

            while (i < _lineEndCount && LineOf(_lineKeys[i]) == line)
            {
                var position = AtOf(_lineKeys[i]);
                while (i < _lineEndCount && LineOf(_lineKeys[i]) == line && AtOf(_lineKeys[i]) - position <= 1)
                {
                    ref readonly var end = ref _lineEnds[i];
                    outward += end.Outward;
                    inward += end.Inward;
                    i++;
                }

                var side = (outward > 0) == (inward > 0) ? 0 : outward > 0 ? 1 : -1;
                if (side == runSide)
                    continue;

                var along = position * EdgeQuantum;
                if (runSide != 0)
                    AddLineSegment(vertical, at, runFrom, along, runSide, eye, radius, segments);

                runSide = side;
                runFrom = along;
            }
        }
    }

    private const float MaxFovSegmentLength = 64f;

    private static void AddLineSegment(bool vertical, float at, float from, float to, int side, Vector2 eye,
        float radius, List<ShadowSegment> segments)
    {
        var a = vertical ? new Vector2(at, from) : new Vector2(from, at);
        var b = vertical ? new Vector2(at, to) : new Vector2(to, at);
        var normal = vertical ? new Vector2(side, 0f) : new Vector2(0f, side);

        if (Vector2.Dot(normal, eye - a) >= 0f || DistanceToSegment(eye, a, b) > radius)
            return;

        // in pieces, like the edges of the lights
        var pieces = Math.Max(1, (int)MathF.Ceiling((to - from) / MaxFovSegmentLength));
        for (var p = 0; p < pieces; p++)
            segments.Add(new ShadowSegment(Vector2.Lerp(a, b, p / (float)pieces), Vector2.Lerp(a, b, (p + 1) / (float)pieces)));
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
                    yield return (a, b, _edgeCounts.TryGetValue(KeyOf(a, b), out var count) && count > 1);
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
            var fovHeight = 0f;
            if (TryComp<OccluderComponent>(uid, out var comp) && comp.Enabled && !HasComp<ContainedComponent>(uid)
                && Transform(uid) is { } transform)
            {
                fovHeight = MathF.Max(comp.FovHeight, 0f);
                _highFovHeight = MathF.Max(_highFovHeight, fovHeight);
                points = BuildOutline(uid, comp, transform);
                if (points.Length < 3)
                    points = null;
            }

            // the same outline as before changes no shadow, the lights around it can stay as they are
            if (points is not null && _entries.TryGetValue(uid, out var old) && old.Points.AsSpan().SequenceEqual(points))
            {
                old.FovHeight = fovHeight;
                continue;
            }

            Forget(uid);

            if (points is not null)
                Add(uid, points, fovHeight);
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

    private void Add(EntityUid uid, Vector2[] points, float fovHeight)
    {
        var entry = new Entry { Points = points, Normals = new Vector2[points.Length], FovHeight = fovHeight };

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
        entry.IsAxisBox = IsAxisBox(points, entry.Bounds);

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

            var key = KeyOf(a, b);
            _edgeCounts[key] = _edgeCounts.GetValueOrDefault(key) + 1;
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
        {
            var key = KeyOf(entry.Points[i], entry.Points[(i + 1) % entry.Points.Length]);
            if (!_edgeCounts.TryGetValue(key, out var count))
                continue;

            if (count <= 1)
                _edgeCounts.Remove(key);
            else
                _edgeCounts[key] = count - 1;
        }
    }

    private static bool IsAxisBox(Vector2[] points, RectangleF bounds)
    {
        if (points.Length != 4)
            return false;

        foreach (var point in points)
        {
            var onX = MathF.Abs(point.X - bounds.Left) < EdgeQuantum || MathF.Abs(point.X - bounds.Right) < EdgeQuantum;
            var onY = MathF.Abs(point.Y - bounds.Top) < EdgeQuantum || MathF.Abs(point.Y - bounds.Bottom) < EdgeQuantum;
            if (!onX || !onY)
                return false;
        }

        return true;
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
    private static bool Contains(Entry entry, Vector2 point, float inside = 0f)
    {
        if (point.X < entry.Bounds.Left || point.X > entry.Bounds.Right
            || point.Y < entry.Bounds.Top || point.Y > entry.Bounds.Bottom)
            return false;

        for (var i = 0; i < entry.Points.Length; i++)
        {
            if (Vector2.Dot(entry.Normals[i], point - entry.Points[i]) > -inside)
                return false;
        }

        return true;
    }

    // how far inside an occluder an eye has to be for it to see out of it
    private const float FovInside = 2f;

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
