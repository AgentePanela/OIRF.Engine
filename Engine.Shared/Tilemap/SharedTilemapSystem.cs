using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Engine.Shared.Prototypes;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Tilemap;

/// <summary>
/// Manages the logic for the tilemaps entities, client version manages rendering it.
/// </summary>
public abstract class SharedTilemapSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager _proto = default!;

    public void AddChunk(TilemapComponent comp, TilemapChunk chunk)
    {
        comp.Chunks[(chunk.ChunkX, chunk.ChunkY)] = chunk;
    }

    public void RemoveChunk(TilemapComponent comp, int cx, int cy)
    {
        comp.Chunks.Remove((cx, cy));
    }

    public TilemapChunk? GetChunk(TilemapComponent comp, int cx, int cy)
    {
        comp.Chunks.TryGetValue((cx, cy), out var chunk);
        return chunk;
    }

    public void SetTile(TilemapComponent comp, int worldTileX, int worldTileY, ProtoId<TilePrototype>? tile)
    {
        int cx = (int)Math.Floor((float)worldTileX / comp.ChunkSize);
        int cy = (int)Math.Floor((float)worldTileY / comp.ChunkSize);

        var chunk = GetChunk(comp, cx, cy);
        if (chunk is null)
            return;

        int localX = worldTileX - cx * comp.ChunkSize;
        int localY = worldTileY - cy * comp.ChunkSize;
        chunk.Tiles[localX, localY] = tile;
        chunk.Dirty = true;
        chunk.SolidTileCount = null;
    }

    public ProtoId<TilePrototype>? GetTile(TilemapComponent comp, int worldTileX, int worldTileY)
    {
        int cx = (int)Math.Floor((float)worldTileX / comp.ChunkSize);
        int cy = (int)Math.Floor((float)worldTileY / comp.ChunkSize);

        var chunk = GetChunk(comp, cx, cy);
        if (chunk is null)
            return null;

        int localX = worldTileX - cx * comp.ChunkSize;
        int localY = worldTileY - cy * comp.ChunkSize;
        return chunk.Tiles[localX, localY];
    }

    public void Clear(TilemapComponent comp)
    {
        foreach (var chunk in comp.Chunks.Values)
            chunk.Dirty = true;

        comp.Chunks.Clear();
    }

    /// <summary>
    /// Converts a world pos to a tile.
    /// </summary>
    public Point WorldToTile(TilemapComponent comp, Vector2 worldPos)
    {
        int tileX = (int)Math.Floor(worldPos.X / comp.TileSize);
        int tileY = (int)Math.Floor(worldPos.Y / comp.TileSize);
        return new Point(tileX, tileY);
    }

    /// <summary>
    /// Converts a tile pos into a world pos.
    /// </summary>
    public Vector2 TileToWorld(TilemapComponent comp, TransformComponent trans, int tileX, int tileY)
    {
        return new Vector2(
            trans.Position.X + tileX * comp.TileSize,
            trans.Position.Y + tileY * comp.TileSize
        );
    }

    /// <summary>
    /// Converts a tile to chunk position
    /// </summary>
    public Point TileToChunk(TilemapComponent comp, int tileX, int tileY)
    {
        int cx = (int)Math.Floor((float)tileX / comp.ChunkSize);
        int cy = (int)Math.Floor((float)tileY / comp.ChunkSize);
        return new Point(cx, cy);
    }

    /// <summary>
    /// Converts a chunk to world position
    /// </summary>
    public Vector2 ChunkToWorld(TilemapComponent comp, TransformComponent? trans, int cx, int cy)
    {
        var pos = trans?.Position ?? Vector2.Zero;
        int worldChunkSize = comp.ChunkSize * comp.TileSize;

        return new Vector2(
            pos.X + cx * worldChunkSize,
            pos.Y + cy * worldChunkSize
        );
    }

    /// <summary>
    /// Converts a tile global position to the tile in the chunk position.
    /// </summary>
    public Point TileToLocal(TilemapComponent comp, int tileX, int tileY)
    {
        var chunk = TileToChunk(comp, tileX, tileY);

        int localX = tileX - chunk.X * comp.ChunkSize;
        int localY = tileY - chunk.Y * comp.ChunkSize;

        return new Point(localX, localY);
    }

    /// <summary>
    /// Returns true if the tile at the given world tile coordinates is solid (blocks movement)
    /// </summary>
    public bool IsTileSolid(TilemapComponent comp, int worldTileX, int worldTileY)
    {
        var tileId = GetTile(comp, worldTileX, worldTileY);
        if (tileId is null)
            return false;

        if (!_proto.TryIndex(tileId.Value, out var proto))
            return false;

        return proto.Solid;
    }

    /// <summary>
    /// Finds all solid tile AABBs (in world pixels) that overlap the given pixel-space rectangle.
    /// </summary>
    public void GetSolidTilesInArea(TilemapComponent comp, TransformComponent tilemapTransform,
        Rectangle area, List<Rectangle> results, List<Point>? tileCoords = null)
    {
        results.Clear();
        tileCoords?.Clear();

        if (comp.Chunks.Count == 0)
            return;

        int tileSize = comp.TileSize;
        int chunkSize = comp.ChunkSize;
        float originX = tilemapTransform.Position.X;
        float originY = tilemapTransform.Position.Y;

        // convert area to tile cordinates
        int tileMinX = (int)Math.Floor((area.Left - originX) / (float)tileSize);
        int tileMinY = (int)Math.Floor((area.Top - originY) / (float)tileSize);
        int tileMaxX = (int)Math.Floor((area.Right - originX) / (float)tileSize);
        int tileMaxY = (int)Math.Floor((area.Bottom - originY) / (float)tileSize);

        int chunkMinX = (int)Math.Floor(tileMinX / (float)chunkSize);
        int chunkMinY = (int)Math.Floor(tileMinY / (float)chunkSize);
        int chunkMaxX = (int)Math.Floor(tileMaxX / (float)chunkSize);
        int chunkMaxY = (int)Math.Floor(tileMaxY / (float)chunkSize);

        // walk chunks instead of cells: empty regions cost one dictionary miss
        // instead of a lookup per tile, and a chunk with no solid tile at all
        // is skipped without touching its grid
        for (int cy = chunkMinY; cy <= chunkMaxY; cy++)
        {
            for (int cx = chunkMinX; cx <= chunkMaxX; cx++)
            {
                if (!comp.Chunks.TryGetValue((cx, cy), out var chunk))
                    continue;
                if (GetSolidTileCount(chunk) == 0)
                    continue;

                int baseX = cx * chunkSize;
                int baseY = cy * chunkSize;
                int last = chunk.Size - 1;

                int loX = Math.Max(0, tileMinX - baseX);
                int hiX = Math.Min(last, tileMaxX - baseX);
                int loY = Math.Max(0, tileMinY - baseY);
                int hiY = Math.Min(last, tileMaxY - baseY);

                for (int ly = loY; ly <= hiY; ly++)
                {
                    for (int lx = loX; lx <= hiX; lx++)
                    {
                        var tileId = chunk.Tiles[lx, ly];
                        if (tileId is null)
                            continue;
                        if (!_proto.TryIndex(tileId.Value, out var proto) || !proto.Solid)
                            continue;

                        results.Add(new Rectangle(
                            (int)(originX + (baseX + lx) * tileSize),
                            (int)(originY + (baseY + ly) * tileSize),
                            tileSize,
                            tileSize));
                        tileCoords?.Add(new Point(baseX + lx, baseY + ly));
                    }
                }
            }
        }
    }

    private int GetSolidTileCount(TilemapChunk chunk)
    {
        if (chunk.SolidTileCount is { } cached)
            return cached;

        int size = chunk.Size;
        int count = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var tileId = chunk.Tiles[x, y];
                if (tileId is null) continue;
                if (_proto.TryIndex(tileId.Value, out var proto) && proto.Solid)
                    count++;
            }
        }

        chunk.SolidTileCount = count;
        return count;
    }
}
