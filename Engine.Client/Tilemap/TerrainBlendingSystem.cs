using System;
using System.Collections.Generic;
using Engine.Client.Assets;
using Engine.Shared.GameObjects;
using Engine.Shared.Prototypes;
using Engine.Client.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Tilemap;

/*
I dont like this code, it was made with help of AI and it is a mess, i dont like it, 
but works and i did not know how to a great implementation for this using the engine
i have plans on rework and maybe use shaders, but since the current tilemap is static, i dont think
we have to worry about it
*/

public sealed class TerrainBlendingSystem : EntitySystem
{
    [Dependency] private readonly IAssetManager _assetMan = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    // how much of the tile edge is covered by the blend (0.x = x%)
    private const float BlendCover = 0.75f;

    // pixel data extracted from atlas for each tile prototype
    private readonly Dictionary<string, Color[]> _tilePixels = new();
    private readonly Dictionary<string, int> _tilePriority = new();

    private readonly Dictionary<int, MaskSet> _maskSets = new();

    // blended overlay cache: key = "selfId_neighborId_bitmask_tileSize"
    private readonly Dictionary<string, Texture2D> _blendCache = new();

    private record MaskSet(
        Color[] Up, Color[] Down, Color[] Left, Color[] Right,
        Color[] TL, Color[] TR, Color[] BL, Color[] BR, int Size);

    // bitmask bits
    private const int BIT_UP = 1;
    private const int BIT_RIGHT = 2;
    private const int BIT_DOWN = 4;
    private const int BIT_LEFT = 8;
    private const int BIT_TL = 16;
    private const int BIT_TR = 32;
    private const int BIT_BL = 64;
    private const int BIT_BR = 128;

    public override void Init()
    {
        base.Init();
        SubscribeEvent<LoadingFinishedEvent>(OnLoaded);
    }

    private void OnLoaded(LoadingFinishedEvent args)
    {
        CacheAllTilePixels();
    }

    private void CacheAllTilePixels()
    {
        var protos = _proto.GetAll<TilePrototype>();
        foreach ((_, var proto) in protos)
        {
            if (_tilePixels.ContainsKey(proto.ID))
                continue;

            if (!_assetMan.GetTexture(proto.Sprite, out var spr, out var page))
                continue;

            var pixels = new Color[spr.Width * spr.Height];
            page.Texture.GetData(0, spr.Region, pixels, 0, pixels.Length);
            _tilePixels[proto.ID] = pixels;
            _tilePriority[proto.ID] = proto.BlendPriority;
        }
    }

    private MaskSet GetOrCreateMasks(int tileSize)
    {
        if (_maskSets.TryGetValue(tileSize, out var existing))
            return existing;

        var set = new MaskSet(
            Up: CreateLinearMask(tileSize, 0, -1),
            Down: CreateLinearMask(tileSize, 0, 1),
            Left: CreateLinearMask(tileSize, -1, 0),
            Right: CreateLinearMask(tileSize, 1, 0),
            TL: CreateCornerMask(tileSize, 0, 0),
            TR: CreateCornerMask(tileSize, tileSize - 1, 0),
            BL: CreateCornerMask(tileSize, 0, tileSize - 1),
            BR: CreateCornerMask(tileSize, tileSize - 1, tileSize - 1),
            Size: tileSize
        );

        _maskSets[tileSize] = set;
        return set;
    }

    /// <summary>
    /// Get (or create and cache) the blended overlay texture for a tile at the given world position.
    /// Returns null if no blending is needed
    /// </summary>
    public Texture2D? GetBlendOverlay(TilemapComponent comp, int worldTileX, int worldTileY, int tileSize)
    {
        var selfId = GetTileAtWorld(comp, worldTileX, worldTileY);
        if (selfId is null)
            return null;

        if (!_tilePriority.TryGetValue(selfId, out int selfPriority))
            return null;

        // check 8 neighbors, build bitmask and find dominant neighbor
        int bitmask = 0;
        string? dominantNeighbor = null;
        int dominantPriority = -1;

        CheckNeighbor(comp, worldTileX, worldTileY - 1, selfId, selfPriority, BIT_UP, ref bitmask, ref dominantNeighbor, ref dominantPriority);
        CheckNeighbor(comp, worldTileX + 1, worldTileY, selfId, selfPriority, BIT_RIGHT, ref bitmask, ref dominantNeighbor, ref dominantPriority);
        CheckNeighbor(comp, worldTileX, worldTileY + 1, selfId, selfPriority, BIT_DOWN, ref bitmask, ref dominantNeighbor, ref dominantPriority);
        CheckNeighbor(comp, worldTileX - 1, worldTileY, selfId, selfPriority, BIT_LEFT, ref bitmask, ref dominantNeighbor, ref dominantPriority);
        CheckNeighbor(comp, worldTileX - 1, worldTileY - 1, selfId, selfPriority, BIT_TL, ref bitmask, ref dominantNeighbor, ref dominantPriority);
        CheckNeighbor(comp, worldTileX + 1, worldTileY - 1, selfId, selfPriority, BIT_TR, ref bitmask, ref dominantNeighbor, ref dominantPriority);
        CheckNeighbor(comp, worldTileX - 1, worldTileY + 1, selfId, selfPriority, BIT_BL, ref bitmask, ref dominantNeighbor, ref dominantPriority);
        CheckNeighbor(comp, worldTileX + 1, worldTileY + 1, selfId, selfPriority, BIT_BR, ref bitmask, ref dominantNeighbor, ref dominantPriority);
        if (bitmask == 0 || dominantNeighbor is null)
            return null;

        // cache key
        string cacheKey = $"{selfId}_{dominantNeighbor}_{bitmask}_{tileSize}";
        if (_blendCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var overlay = CreateBlendedOverlay(selfId, dominantNeighbor, bitmask, tileSize);
        if (overlay is not null)
            _blendCache[cacheKey] = overlay;

        return overlay;
    }

    private void CheckNeighbor(TilemapComponent comp, int nx, int ny, string selfId, int selfPriority,
        int bit, ref int bitmask, ref string? dominantNeighbor, ref int dominantPriority)
    {
        var neighborId = GetTileAtWorld(comp, nx, ny);
        if (neighborId is null || neighborId == selfId)
            return;

        if (!_tilePriority.TryGetValue(neighborId, out int neighborPriority))
            return;

        // only blend if neighbor has HIGHER priority (it "grows" over us)
        if (neighborPriority <= selfPriority)
            return;

        bitmask |= bit;

        if (neighborPriority > dominantPriority)
        {
            dominantPriority = neighborPriority;
            dominantNeighbor = neighborId;
        }
    }

    private string? GetTileAtWorld(TilemapComponent comp, int wx, int wy)
    {
        int cx = (int)Math.Floor((float)wx / comp.ChunkSize);
        int cy = (int)Math.Floor((float)wy / comp.ChunkSize);

        if (!comp.Chunks.TryGetValue((cx, cy), out var chunk))
            return null;

        int lx = wx - cx * comp.ChunkSize;
        int ly = wy - cy * comp.ChunkSize;

        if (lx < 0 || lx >= chunk.Size || ly < 0 || ly >= chunk.Size)
            return null;

        var tileId = chunk.Tiles[lx, ly];
        return tileId.HasValue ? (string)tileId.Value : null;
    }

    private Texture2D? CreateBlendedOverlay(string selfId, string neighborId, int bitmask, int tileSize)
    {
        if (!_tilePixels.TryGetValue(selfId, out var selfPixels) ||
            !_tilePixels.TryGetValue(neighborId, out var neighborPixels))
            return null;

        var masks = GetOrCreateMasks(tileSize);
        int total = tileSize * tileSize;

        // build combined mask from bitmask
        var combinedMask = new float[total];
        CombineMask(combinedMask, masks.Up, bitmask, BIT_UP);
        CombineMask(combinedMask, masks.Right, bitmask, BIT_RIGHT);
        CombineMask(combinedMask, masks.Down, bitmask, BIT_DOWN);
        CombineMask(combinedMask, masks.Left, bitmask, BIT_LEFT);
        CombineMask(combinedMask, masks.TL, bitmask, BIT_TL);
        CombineMask(combinedMask, masks.TR, bitmask, BIT_TR);
        CombineMask(combinedMask, masks.BL, bitmask, BIT_BL);
        CombineMask(combinedMask, masks.BR, bitmask, BIT_BR);

        var result = new Color[total];
        for (int i = 0; i < total; i++)
        {
            float m = combinedMask[i];
            if (m <= 0f)
            {
                result[i] = Color.Transparent;
                continue;
            }

            var a = GetPixelSafe(selfPixels, i, total);
            var b = GetPixelSafe(neighborPixels, i, total);

            result[i] = Color.Lerp(a, b, m);
        }

        var tex = new Texture2D(GameClient.GraphicsDevice, tileSize, tileSize);
        tex.SetData(result);
        return tex;
    }

    private static Color GetPixelSafe(Color[] pixels, int index, int expectedTotal)
    {
        if (index < pixels.Length)
            return pixels[index];
        return Color.Transparent;
    }

    private static void CombineMask(float[] output, Color[] mask, int bitmask, int bit)
    {
        if ((bitmask & bit) == 0)
            return;

        for (int i = 0; i < output.Length && i < mask.Length; i++)
        {
            float v = mask[i].R / 255f;
            if (v > output[i])
                output[i] = v;
        }
    }

    #region Mask Generation

    /// <summary>
    /// Creates a linear gradient mask from one edge.
    /// dirX/dirY: -1=from left/top edge, +1=from right/bottom edge, 0=not this axis
    /// </summary>
    private static Color[] CreateLinearMask(int size, int dirX, int dirY)
    {
        var data = new Color[size * size];
        float falloff = Math.Max(1f, size * BlendCover);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float t = 0f;

                if (dirX == -1) // from left edge
                    t = (x < falloff) ? 1f - (x / falloff) : 0f;
                else if (dirX == 1) // from right edge
                    t = (x > size - 1 - falloff) ? (x - (size - 1 - falloff)) / falloff : 0f;
                else if (dirY == -1) // from top edge
                    t = (y < falloff) ? 1f - (y / falloff) : 0f;
                else if (dirY == 1) // from bottom edge
                    t = (y > size - 1 - falloff) ? (y - (size - 1 - falloff)) / falloff : 0f;

                t = MathHelper.Clamp(t, 0f, 1f);
                byte m = (byte)(t * 255f);
                data[y * size + x] = new Color(m, m, m, m);
            }

        return data;
    }

    /// <summary>
    /// Creates a radial gradient mask from a corner point.
    /// </summary>
    private static Color[] CreateCornerMask(int size, int cornerX, int cornerY)
    {
        var data = new Color[size * size];
        float falloff = Math.Max(1f, size * BlendCover);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - cornerX;
                float dy = y - cornerY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                float t = (dist < falloff) ? 1f - (dist / falloff) : 0f;
                t = MathHelper.Clamp(t, 0f, 1f);
                byte m = (byte)(t * 255f);
                data[y * size + x] = new Color(m, m, m, m);
            }

        return data;
    }

    #endregion

    /// <summary>
    /// Invalidates the blend cache (e.g. after terrain modification).
    /// </summary>
    public void InvalidateCache()
    {
        foreach (var tex in _blendCache.Values)
            tex.Dispose();
        _blendCache.Clear();
    }
}
