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

internal readonly record struct ShadowSegment(Vector2 A, Vector2 B, Vector3 Transmit = default,
    Vector4 Exclude = default, Vector4 Only = default);

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
        /// The light that goes through it. Zero for an opaque one.
        /// </summary>
        public Vector3 Transmit;

        /// <summary>
        /// Light goes through
        /// </summary>
        public bool SeeThrough => Transmit != Vector3.Zero;

        /// <summary>
        /// Its outline is a box along the axes, which the field of view raises by <see cref="FovHeight"/>.
        /// </summary>
        public bool IsAxisBox;

        public bool InRow;
        public int InRowVersion = -1;
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

    internal void CollectFovSegments(Vector2 eye, float radius, List<ShadowSegment> segments)
    {
        Update();

        // the same eye, as far, among the same outlines: the same edges as last time
        if (eye == _fovCacheEye && radius == _fovCacheRadius && _outlinesVersion == _fovCacheVersion)
        {
            segments.AddRange(_fovCache);
            return;
        }

        _lineEndCount = 0;
        _fovEnds.Clear();
        _fovDrawings.Clear();
        _fovRaised.Clear();
        var first = segments.Count;

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
                if (entry.SeeThrough || !Intersects(entry.Bounds, reach) || Contains(entry, eye, FovInside))
                    continue;

                if (entry.IsAxisBox)
                    AddFovBox(entry, eye);
                else
                    AddEdges(entry, eye, radius, segments);
            }
        }

        Array.Sort(_lineKeys, _lineEnds, 0, _lineEndCount);
        AddLineEdges(eye, radius, segments);
        LeaveDrawingsInFront(segments, first);
        AddEndsSeen(eye, segments);

        _fovCache.Clear();
        for (var i = first; i < segments.Count; i++)
            _fovCache.Add(segments[i]);

        _fovCacheEye = eye;
        _fovCacheRadius = radius;
        _fovCacheVersion = _outlinesVersion;
    }

    // the edges of the last CollectFovSegments, and what they were found for
    private readonly List<ShadowSegment> _fovCache = new();
    private Vector2 _fovCacheEye = new(float.NaN);
    private float _fovCacheRadius;
    private int _fovCacheVersion = -1;

    // The lines the sides of box occluders lie on are kept apart by group, and only sides of the same group merge
    private const int ColumnGroup = 0;
    private const int RowTopGroup = 1;
    private const int RowBottomGroup = 2;

    // along y, how wide the box is.
    private readonly record struct LineEnd(int Outward, int Inward, float Raise, float Size);

    // The ends of the sides, sorted by their keys
    private long[] _lineKeys = new long[256];
    private LineEnd[] _lineEnds = new LineEnd[256];
    private int _lineEndCount;

    private const int AtBits = 29;
    private const int LineBits = 30;
    private const int GroupBits = 2;
    private const long AtMask = (1L << AtBits) - 1;
    private const long LineMask = (1L << LineBits) - 1;
    private const long GroupMask = (1L << GroupBits) - 1;

    private static long LineKey(bool vertical, int group, int line, int at)
        => ((vertical ? 1L : 0L) << (GroupBits + LineBits + AtBits))
           | ((group & GroupMask) << (LineBits + AtBits))
           | (((line + (1L << (LineBits - 1))) & LineMask) << AtBits)
           | ((at + (1L << (AtBits - 1))) & AtMask);

    // all but where along the line
    private static long LineOf(long key)
        => key >> AtBits;

    private static int AtOf(long key)
        => (int)((key & AtMask) - (1L << (AtBits - 1)));

    // A side along y of a box, facing the eye
    private readonly record struct FovEnd(float At, float From, float To, int Side, float Width);

    // What a row draws above its top, which is at Y
    private readonly record struct FovDrawing(float From, float To, float Y, float Raise, float Depth);

    private readonly List<FovEnd> _fovEnds = new();
    private readonly List<FovDrawing> _fovDrawings = new();

    // what every wall seen from the front draws above its top, rows and columns
    private readonly List<Vector4> _fovRaised = new();

    private void AddFovBox(Entry entry, Vector2 eye)
    {
        var bounds = entry.Bounds;

        // nothing is drawn above it from where the eye is less than its height north of it
        var behind = eye.Y > bounds.Top - entry.FovHeight && eye.Y <= bounds.Top;
        var raise = behind ? 0f : entry.FovHeight;
        var inRow = IsInRow(entry);

        AddLineEdge(true, ColumnGroup, bounds.Left, bounds.Top, bounds.Bottom, -1, 0f, bounds.Width);
        AddLineEdge(true, ColumnGroup, bounds.Right, bounds.Top, bounds.Bottom, 1, 0f, bounds.Width);
        AddLineEdge(false, inRow ? RowTopGroup : ColumnGroup, bounds.Top, bounds.Left, bounds.Right, -1, raise,
            bounds.Height);
        AddLineEdge(false, inRow ? RowBottomGroup : ColumnGroup, bounds.Bottom, bounds.Left, bounds.Right, 1, 0f, 0f);
    }

    private bool IsInRow(Entry entry)
    {
        if (entry.InRowVersion != _outlinesVersion)
        {
            var b = entry.Bounds;
            var left = KeyOf(new Vector2(b.Left, b.Top), new Vector2(b.Left, b.Bottom));
            var right = KeyOf(new Vector2(b.Right, b.Top), new Vector2(b.Right, b.Bottom));
            entry.InRow = _edgeCounts.GetValueOrDefault(left) > 1 || _edgeCounts.GetValueOrDefault(right) > 1;
            entry.InRowVersion = _outlinesVersion;
        }

        return entry.InRow;
    }

    private void AddLineEdge(bool vertical, int group, float at, float from, float to, int side, float raise,
        float size)
    {
        if (_lineEndCount + 2 > _lineKeys.Length)
        {
            Array.Resize(ref _lineKeys, _lineKeys.Length * 2);
            Array.Resize(ref _lineEnds, _lineEnds.Length * 2);
        }

        var line = (int)MathF.Round(at / EdgeQuantum);
        var outward = side > 0 ? 1 : 0;
        var inward = side > 0 ? 0 : 1;

        _lineKeys[_lineEndCount] = LineKey(vertical, group, line, (int)MathF.Round(from / EdgeQuantum));
        _lineEnds[_lineEndCount++] = new LineEnd(outward, inward, raise, size);
        _lineKeys[_lineEndCount] = LineKey(vertical, group, line, (int)MathF.Round(to / EdgeQuantum));
        _lineEnds[_lineEndCount++] = new LineEnd(-outward, -inward, -raise, -size);
    }

    private void AddLineEdges(Vector2 eye, float radius, List<ShadowSegment> segments)
    {
        var i = 0;
        while (i < _lineEndCount)
        {
            var line = LineOf(_lineKeys[i]);
            var vertical = (line >> (GroupBits + LineBits)) != 0;
            var group = (int)((line >> LineBits) & GroupMask);
            var at = ((line & LineMask) - (1L << (LineBits - 1))) * EdgeQuantum;

            int outward = 0, inward = 0, runSide = 0;
            float raise = 0f, size = 0f, runFrom = 0f, runRaise = 0f, runSize = 0f;

            while (i < _lineEndCount && LineOf(_lineKeys[i]) == line)
            {
                // ends one quantum apart are the same point
                var position = AtOf(_lineKeys[i]);
                while (i < _lineEndCount && LineOf(_lineKeys[i]) == line && AtOf(_lineKeys[i]) - position <= 1)
                {
                    ref readonly var end = ref _lineEnds[i];
                    outward += end.Outward;
                    inward += end.Inward;
                    raise += end.Raise;
                    size += end.Size;
                    i++;
                }

                var side = (outward > 0) == (inward > 0) ? 0 : outward > 0 ? 1 : -1;
                var nowRaise = side == 0 ? 0f : raise;
                var nowSize = side == 0 ? 0f : size;
                if (side == runSide && MathF.Abs(nowRaise - runRaise) < 0.01f && MathF.Abs(nowSize - runSize) < 0.01f)
                    continue;

                var along = position * EdgeQuantum;
                if (runSide != 0)
                    AddFovLine(vertical, group, at, runFrom, along, runSide, runRaise, runSize, eye, radius, segments);

                runSide = side;
                runFrom = along;
                runRaise = nowRaise;
                runSize = nowSize;
            }
        }
    }

    private const float MaxFovSegmentLength = 64f;

    // the length an edge along x is looked at in, to leave out the wall right behind it
    private const float WallProbeLength = 32f;

    private void AddFovLine(bool vertical, int group, float at, float from, float to, int side, float raise, float size,
        Vector2 eye, float radius, List<ShadowSegment> segments)
    {
        var a = vertical ? new Vector2(at, from) : new Vector2(from, at);
        var b = vertical ? new Vector2(at, to) : new Vector2(to, at);
        var normal = vertical ? new Vector2(side, 0f) : new Vector2(0f, side);

        if (DistanceToSegment(eye, a, b) > radius)
            return;

        // facing the eye, its shadow is the one of the far side, but the end of a row seen from beside it may hide
        // the drawing of the row (AddEndsSeen)
        if (Vector2.Dot(normal, eye - a) >= 0f)
        {
            if (vertical)
                _fovEnds.Add(new FovEnd(at, from, to, side, size));
            return;
        }

        if (!vertical && side < 0 && raise > 0f)
        {
            // the top of a wall drawn taller than it is: its shadow leaves the drawing out
            var drawing = new Vector4(from, at - raise, to, at);
            AddPieces(a, b, drawing, default, segments);
            _fovRaised.Add(drawing);
            if (group == RowTopGroup)
                _fovDrawings.Add(new FovDrawing(from, to, at, raise, size));
            return;
        }

        if (vertical)
            AddPieces(a, b, default, default, segments);
        else
            AddAgainstWalls(from, to, at, side, segments);
    }

    private void AddAgainstWalls(float from, float to, float at, int side, List<ShadowSegment> segments)
    {
        var runFrom = from;
        var runWall = default(RectangleF?);

        for (var x = from; x < to - EdgeQuantum; x += WallProbeLength)
        {
            var end = MathF.Min(x + WallProbeLength, to);
            var wall = FindWall(new Vector2((x + end) / 2f, at + side));
            if (wall is { } w && (w.Left > x + EdgeQuantum || w.Right < end - EdgeQuantum))
                wall = null;

            var same = wall is null ? runWall is null : runWall is { } r && r.Top == wall.Value.Top && r.Bottom == wall.Value.Bottom;
            if (x > from && (!same || end - runFrom > MaxFovSegmentLength))
            {
                AddAgainst(runFrom, x, at, runWall, segments);
                runFrom = x;
            }

            runWall = wall;
        }

        AddAgainst(runFrom, to, at, runWall, segments);
    }

    private static void AddAgainst(float from, float to, float at, RectangleF? wall, List<ShadowSegment> segments)
    {
        var exclude = wall is { } w ? new Vector4(from, w.Top, to, w.Bottom) : default;
        AddPieces(new Vector2(from, at), new Vector2(to, at), exclude, default, segments);
    }

    // the opaque box occluder at a point
    private RectangleF? FindWall(Vector2 point)
    {
        if (!_cells.TryGetValue(ToCell(point), out var cell))
            return null;

        foreach (var entry in cell)
        {
            var b = entry.Bounds;
            if (entry.IsAxisBox && !entry.SeeThrough && point.X > b.Left && point.X < b.Right && point.Y > b.Top
                && point.Y < b.Bottom)
                return b;
        }

        return null;
    }

    // in pieces, like the edges of the lights
    private static void AddPieces(Vector2 a, Vector2 b, Vector4 exclude, Vector4 only, List<ShadowSegment> segments)
    {
        var pieces = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / MaxFovSegmentLength));
        for (var p = 0; p < pieces; p++)
        {
            segments.Add(new ShadowSegment(Vector2.Lerp(a, b, p / (float)pieces),
                Vector2.Lerp(a, b, (p + 1) / (float)pieces), default, exclude, only));
        }
    }

    // An edge inside the drawing of a wall seen from the front belongs to something behind that wall
    private void LeaveDrawingsInFront(List<ShadowSegment> segments, int first)
    {
        if (_fovRaised.Count == 0)
            return;

        for (var i = first; i < segments.Count; i++)
        {
            var s = segments[i];
            if (s.Exclude.Z > s.Exclude.X)
                continue;

            var middle = (s.A + s.B) / 2f;
            foreach (var drawing in _fovRaised)
            {
                if (middle.X > drawing.X && middle.X < drawing.Z && middle.Y > drawing.Y && middle.Y < drawing.W)
                {
                    segments[i] = s with { Exclude = drawing };
                    break;
                }
            }
        }
    }

    // how far apart the floor in front of a row is looked at, before the edges between seen and hidden are refined
    private const float FrontStep = 8f;

    private void AddEndsSeen(Vector2 eye, List<ShadowSegment> segments)
    {
        if (_fovEnds.Count == 0 || _fovDrawings.Count == 0)
            return;

        var shadows = segments.Count;

        foreach (var end in _fovEnds)
        {
            // an end behind another wall shows nothing of its row
            var middle = new Vector2(end.At + end.Side, (end.From + end.To) / 2f);
            if (FloorHidden(eye, middle, segments, shadows))
                continue;

            foreach (var drawing in _fovDrawings)
            {
                var atEnd = end.Side > 0 ? MathF.Abs(drawing.To - end.At) < 0.01f : MathF.Abs(drawing.From - end.At) < 0.01f;
                if (!atEnd || drawing.Y < end.From - 0.01f || drawing.Y >= end.To - 0.01f)
                    continue;

                // past the end cell
                var from = end.Side > 0 ? drawing.From : drawing.From + end.Width;
                var to = end.Side > 0 ? drawing.To - end.Width : drawing.To;
                if (to - from > EdgeQuantum)
                    HideBehindEnd(eye, end, drawing, from, to, segments, shadows);
            }
        }
    }

    private static void HideBehindEnd(Vector2 eye, FovEnd end, FovDrawing drawing, float from, float to,
        List<ShadowSegment> segments, int shadows)
    {
        var front = drawing.Y + drawing.Depth + 1f;
        var hiddenFrom = from;
        var wasHidden = FloorHidden(eye, new Vector2(from, front), segments, shadows);

        for (var x = MathF.Min(from + FrontStep, to); ; x = MathF.Min(x + FrontStep, to))
        {
            var hidden = FloorHidden(eye, new Vector2(x, front), segments, shadows);
            if (hidden != wasHidden)
            {
                var change = FindChange(eye, front, x - FrontStep, x, wasHidden, segments, shadows);
                if (hidden)
                    hiddenFrom = change;
                else
                    AddBehindEnd(end, drawing, hiddenFrom, change, segments);

                wasHidden = hidden;
            }

            if (x >= to)
                break;
        }

        if (wasHidden)
            AddBehindEnd(end, drawing, hiddenFrom, to, segments);
    }

    private static void AddBehindEnd(FovEnd end, FovDrawing drawing, float from, float to,
        List<ShadowSegment> segments)
    {
        if (to - from <= EdgeQuantum)
            return;

        // the drawing above the row and the row itself, which is drawn over too
        var only = new Vector4(from, drawing.Y - drawing.Raise, to, drawing.Y + drawing.Depth);
        segments.Add(new ShadowSegment(new Vector2(end.At, end.From), new Vector2(end.At, end.To), default, default,
            only));
    }

    private static float FindChange(Vector2 eye, float front, float from, float to, bool fromHidden,
        List<ShadowSegment> segments, int count)
    {
        while (to - from > 1f)
        {
            var middle = (from + to) / 2f;
            if (FloorHidden(eye, new Vector2(middle, front), segments, count) == fromHidden)
                from = middle;
            else
                to = middle;
        }

        return (from + to) / 2f;
    }

    // whether one of the first count shadows covers point
    private static bool FloorHidden(Vector2 eye, Vector2 point, List<ShadowSegment> segments, int count)
    {
        var minX = MathF.Min(eye.X, point.X);
        var maxX = MathF.Max(eye.X, point.X);
        var minY = MathF.Min(eye.Y, point.Y);
        var maxY = MathF.Max(eye.Y, point.Y);

        for (var i = 0; i < count; i++)
        {
            var s = segments[i];
            if (MathF.Max(s.A.X, s.B.X) < minX || MathF.Min(s.A.X, s.B.X) > maxX
                || MathF.Max(s.A.Y, s.B.Y) < minY || MathF.Min(s.A.Y, s.B.Y) > maxY)
                continue;

            var ex = s.Exclude;
            if (ex.Z > ex.X && point.X > ex.X && point.X < ex.Z && point.Y > ex.Y && point.Y < ex.W)
                continue;

            if (Crosses(eye, point, s.A, s.B))
                return true;
        }

        return false;
    }

    private static bool Crosses(Vector2 p, Vector2 q, Vector2 a, Vector2 b)
    {
        return Cross(a, b, p) * Cross(a, b, q) < 0f && Cross(p, q, a) * Cross(p, q, b) < 0f;

        static float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.X - o.X) * (v.Y - o.Y) - (u.Y - o.Y) * (v.X - o.X);
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
            var fovHeight = 0f;
            var transmit = Vector3.Zero;
            if (TryComp<OccluderComponent>(uid, out var comp) && comp.Enabled && !HasComp<ContainedComponent>(uid)
                && Transform(uid) is { } transform)
            {
                fovHeight = MathF.Max(comp.FovHeight, 0f);
                transmit = comp.Tint.ToVector3() * (1f - Math.Clamp(comp.Opacity, 0f, 1f));
                _highFovHeight = MathF.Max(_highFovHeight, fovHeight);
                points = BuildOutline(uid, comp, transform);
                if (points.Length < 3)
                    points = null;
            }

            // the same outline as before changes no shadow, the lights around it can stay as they are
            if (points is not null && _entries.TryGetValue(uid, out var old) && old.Transmit == transmit
                && old.Points.AsSpan().SequenceEqual(points))
            {
                if (old.FovHeight != fovHeight)
                {
                    old.FovHeight = fovHeight;
                    _outlinesVersion++;
                }

                continue;
            }

            Forget(uid);

            if (points is not null)
                Add(uid, points, fovHeight, transmit);
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

    private void Add(EntityUid uid, Vector2[] points, float fovHeight, Vector3 transmit)
    {
        var entry = new Entry
        {
            Points = points,
            Normals = new Vector2[points.Length],
            FovHeight = fovHeight,
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
