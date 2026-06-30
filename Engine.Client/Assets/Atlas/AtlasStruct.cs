using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RectangleBinPacking;

namespace Engine.Client.Assets.Atlas;

/// <summary>
/// The atlas page size references.
/// </summary>
public enum AtlasSize : uint
{
    /// <summary>
    /// Recommended for earlier iPhone/iPod touch models.
    /// </summary>
    Size1024 = 1024,
    /// <summary>
    /// Recommended for older cards, most Intel chips - <strong>BETTER CHOICE</strong>.
    /// </summary>
    Size2048 = 2048,
    /// <summary>
    /// Recommended for newer cards.
    /// </summary>
    Size4096 = 4096,
    /// <summary>
    /// Wow, thats big.
    /// </summary>
    Size8192 = 8192
}

public struct AtlasSprite
{
    public int Page;
    public Rectangle Region;
    public int Width;
    public int Height;

    public AtlasSprite(int page, Rectangle region, int width, int height)
    {
        Page = page;
        Region = region;
        Width = width;
        Height = height;
    }
}

public sealed class AtlasPage : IDisposable
{
    public readonly uint Size;
    public Texture2D Texture;
    public Dictionary<string, Rectangle> Regions = new();
    private int _idCounter = 1;

    public bool Locked;
    /// <summary>
    /// Not made yet. It would need to adptad the render manager to this and the performance gain dont worth it.
    /// </summary>
    public bool AllowRotation { get; set; } = false;

    private readonly MaxRectsBinPack<int> _packer;

    public AtlasPage(uint size, bool locked = false)
    {
        if (size == 0) throw new ArgumentException("size must be > 0", nameof(size));
        Size = size;
        Locked = locked;

        // Heurística escolhida: RectBestAreaFit (boa para sprites variados).
        // Último parâmetro = allowRotation (mantemos false por compatibilidade com Copy atual).
        _packer = new MaxRectsBinPack<int>((int)size, (int)size, FreeRectChoiceHeuristic.RectBestAreaFit, AllowRotation);
    }

    public void Dispose()
    {
        Locked = true;
        Texture.Dispose();
    }

    /// <summary>
    /// Try to allocate a sprite region to the best location in the current region
    /// </summary>
    public bool TryPlace(int width, int height, uint padding, out Rectangle region)
    {
        region = default;
        
        if (Locked)
            return false;
        if (width <= 0 || height <= 0)
            return false;

        int reqW = width + (int)padding * 2;
        int reqH = height + (int)padding * 2;
        if (reqW <= 0 || reqH <= 0)
            return false;

        int id = _idCounter++;
        var res = _packer.Insert(id, reqW, reqH);
        if (res is null)
            return false;

        var placedRect = new Rectangle(res.X, res.Y, reqW, reqH);

        region = new Rectangle(
            placedRect.X + (int)padding,
            placedRect.Y + (int)padding,
            width,
            height
        );

        return true;
    }
}
