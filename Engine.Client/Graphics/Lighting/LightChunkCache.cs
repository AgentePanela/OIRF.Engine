using System;
using System.Collections.Generic;
using Engine.Shared.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// The static light of the world, summed in square pieces kept in big textures. Only the pieces near a
/// viewport are kept.
/// </summary>
internal sealed class LightChunkCache : IDisposable
{
    /// <summary>
    /// World units on a side of a piece.
    /// </summary>
    public const float ChunkSize = 512f;

    public const int PageSize = 2048;
    public const int MaxPages = 4;

    /// <summary>
    /// Texels drawn past every side of a piece, so smoothing at its edge reads its own light and not the next piece.
    /// </summary>
    public const int Border = 1;

    public sealed class Chunk
    {
        public (int X, int Y) Coords;
        public int Slot;

        /// <summary>
        /// Has to be drawn again before it is used.
        /// </summary>
        public bool Dirty = true;

        // the last frame a viewport wanted it
        public int Wanted;
    }

    private readonly List<RenderTarget2D> _pages = new();
    private readonly Dictionary<(int X, int Y), Chunk> _resident = new();
    private readonly Stack<int> _free = new();
    private readonly HashSet<(int X, int Y)> _want = new();
    private readonly List<(int X, int Y)> _scratch = new();

    private int _perRow;

    /// <summary>
    /// Texels on a side of a piece, its border left out.
    /// </summary>
    public int ChunkTexels { get; private set; }

    /// <summary>
    /// Texels per world unit, what the pieces are drawn at.
    /// </summary>
    public float Scale { get; private set; }

    public int Capacity => _perRow * _perRow * MaxPages;

    public IReadOnlyCollection<Chunk> Resident => _resident.Values;

    public long Bytes => _pages.Count * (long)PageSize * PageSize * 4;

    /// <summary>
    /// Throws every piece away and starts over at <paramref name="scale"/> texels per world unit.
    /// </summary>
    public void Reset(float scale)
    {
        Scale = scale;
        ChunkTexels = (int)MathF.Ceiling(ChunkSize * scale);
        _perRow = Math.Max(0, PageSize / (ChunkTexels + Border * 2));

        _resident.Clear();
        _free.Clear();
        for (var slot = Capacity - 1; slot >= 0; slot--)
            _free.Push(slot);
    }

    /// <summary>
    /// Keeps the pieces the light maps in <paramref name="areas"/> need, and one more ring around them.
    /// </summary>
    public bool Update(List<RectangleF> areas, int frame)
    {
        _want.Clear();
        foreach (var area in areas)
            AddCoords(Grow(area, ChunkSize), _want);

        // a ring of slack, so walking back and forth over the edge of a piece does not draw it again every time
        _scratch.Clear();
        foreach (var (coords, chunk) in _resident)
        {
            if (_want.Contains(coords))
                continue;

            var keep = false;
            foreach (var area in areas)
            {
                if (Intersects(Grow(area, ChunkSize * 2f), WorldRect(coords)))
                {
                    keep = true;
                    break;
                }
            }

            if (!keep)
                _scratch.Add(coords);
        }

        foreach (var coords in _scratch)
            Evict(coords);

        if (_want.Count > Capacity)
            return false;

        foreach (var coords in _want)
        {
            if (_resident.TryGetValue(coords, out var chunk))
            {
                chunk.Wanted = frame;
                continue;
            }

            if (_free.Count == 0 && !EvictUnwanted(frame))
                return false;

            _resident[coords] = new Chunk { Coords = coords, Slot = _free.Pop(), Wanted = frame };
        }

        return true;
    }

    /// <summary>
    /// The pieces over <paramref name="area"/> have to be drawn again.
    /// </summary>
    public void MarkDirty(RectangleF area)
    {
        _scratch.Clear();
        AddCoords(area, _scratch);
        foreach (var coords in _scratch)
        {
            if (_resident.TryGetValue(coords, out var chunk))
                chunk.Dirty = true;
        }
    }

    public static RectangleF WorldRect((int X, int Y) coords)
        => new(coords.X * ChunkSize, coords.Y * ChunkSize, ChunkSize, ChunkSize);

    /// <summary>
    /// Where a piece is: its page, and its square there with the border.
    /// </summary>
    public (int Page, Rectangle Square) Locate(int slot)
    {
        var perPage = _perRow * _perRow;
        var index = slot % perPage;
        var size = ChunkTexels + Border * 2;
        return (slot / perPage, new Rectangle(index % _perRow * size, index / _perRow * size, size, size));
    }

    public RenderTarget2D GetPage(GraphicsDevice device, int index)
    {
        while (_pages.Count <= index)
            _pages.Add(CreatePage(device));

        if (_pages[index].IsDisposed)
            _pages[index] = CreatePage(device);

        return _pages[index];
    }

    // a piece that did not change has to survive the others being drawn around it
    private static RenderTarget2D CreatePage(GraphicsDevice device)
        => new(device, PageSize, PageSize, false, SurfaceFormat.Color, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);

    // makes room by dropping a piece kept only as slack, the one wanted the longest ago
    private bool EvictUnwanted(int frame)
    {
        Chunk? oldest = null;
        foreach (var chunk in _resident.Values)
        {
            if (chunk.Wanted != frame && (oldest is null || chunk.Wanted < oldest.Wanted))
                oldest = chunk;
        }

        if (oldest is null)
            return false;

        Evict(oldest.Coords);
        return true;
    }

    private void Evict((int X, int Y) coords)
    {
        if (_resident.Remove(coords, out var chunk))
            _free.Push(chunk.Slot);
    }

    private static void AddCoords(RectangleF area, ICollection<(int X, int Y)> into)
    {
        var minX = (int)MathF.Floor(area.Left / ChunkSize);
        var minY = (int)MathF.Floor(area.Top / ChunkSize);
        var maxX = (int)MathF.Floor(area.Right / ChunkSize);
        var maxY = (int)MathF.Floor(area.Bottom / ChunkSize);

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
            into.Add((x, y));
    }

    private static RectangleF Grow(RectangleF area, float by)
        => new(area.X - by, area.Y - by, area.Width + by * 2f, area.Height + by * 2f);

    private static bool Intersects(RectangleF a, RectangleF b)
        => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    public void Dispose()
    {
        foreach (var page in _pages)
            page.Dispose();

        _pages.Clear();
    }
}
