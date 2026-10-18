using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// One light to put in the light map
/// </summary>
internal struct LightDraw
{
    public EntityUid Owner;

    public Vector2 Center;
    public float Radius;
    public float Rotation;
    public Color Color;
    public float Brightness;

    public float HalfCone;

    public float ConeSoftness;
    public float Falloff;
    public float Curve;

    public Texture2D? Mask;

    public Rectangle MaskRegion;
    public bool CastShadows;
    public float SourceRadius;

    /// <summary>
    /// The atlas page it is drawn in, -1 when there was no room.
    /// </summary>
    public int Page;

    public Rectangle Region;

    /// <summary>
    /// Page pixels per world unit.
    /// </summary>
    public float SlotScale;
}

internal sealed class LightAtlas : IDisposable
{
    public const int PageSize = 2048;

    /// <summary>
    /// Empty pixels around every light, so smoothing never reads the light next door.
    /// </summary>
    public const int Padding = 2;

    public const int MaxPages = 8;

    // squares come in sizes of this many pixels, it keeps the rows of a page even
    private const int Step = 32;

    private readonly List<RenderTarget2D> _pages = new();

    // filling a page in rows, the tallest square of the row sets its height
    private int _page;
    private int _x;
    private int _y;
    private int _rowHeight;

    /// <summary>
    /// How many pages the squares placed since <see cref="Reset"/> take.
    /// </summary>
    public int PagesUsed { get; private set; }

    public long Bytes => _pages.Count * (long)PageSize * PageSize * 4;

    public static int RegionSize(float radius, float scale, out float slotScale)
    {
        slotScale = MathF.Min(scale, (PageSize - Padding * 2) / MathF.Max(radius * 2f, 1f));
        var size = (int)MathF.Ceiling(radius * 2f * slotScale) + Padding * 2;
        return Math.Min(PageSize, (size + Step - 1) / Step * Step);
    }

    public void Reset()
    {
        _page = _x = _y = _rowHeight = 0;
        PagesUsed = 0;
    }

    public bool TryPlace(int size, out int page, out Point at)
    {
        if (_x + size > PageSize)
        {
            _y += _rowHeight;
            _x = 0;
            _rowHeight = 0;
        }

        if (_y + size > PageSize)
        {
            _page++;
            _x = _y = _rowHeight = 0;
        }

        if (_page >= MaxPages)
        {
            page = -1;
            at = Point.Zero;
            return false;
        }

        page = _page;
        at = new Point(_x, _y);
        _x += size;
        _rowHeight = Math.Max(_rowHeight, size);
        PagesUsed = Math.Max(PagesUsed, _page + 1);
        return true;
    }

    public RenderTarget2D GetPage(GraphicsDevice device, int index)
    {
        while (_pages.Count <= index)
            _pages.Add(CreatePage(device));

        if (_pages[index].IsDisposed)
            _pages[index] = CreatePage(device);

        return _pages[index];
    }

    // kept between frames, a light that did not change can stay where it is
    private static RenderTarget2D CreatePage(GraphicsDevice device)
        => new(device, PageSize, PageSize, false, SurfaceFormat.Color, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);

    public void Dispose()
    {
        foreach (var page in _pages)
            page.Dispose();

        _pages.Clear();
    }
}
