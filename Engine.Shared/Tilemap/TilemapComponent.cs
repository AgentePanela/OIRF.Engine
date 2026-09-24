using System.Collections.Generic;
using Engine.Shared.Assets;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Shared.Tilemap;

[RegisterComponent("Tilemap"), NetworkedComponent]
public sealed partial class TilemapComponent : Component
{
    [NetField] public int TileSize { get; set; } = 128;
    [NetField] public int ChunkSize { get; set; } = 16;
    [NetField] public int Layer { get; set; } = 0;

    #region  Client-Side
    public SamplerState? SamplerState { get; set; }
    public bool TileBlending { get; set; } = true;
    
    [ShaderKey, NetField]
    public string? Shader { get; set; }
    #endregion

    public Dictionary<(int, int), TilemapChunk> Chunks { get; set; } = new();
}

public sealed class TilemapChunk
{
    public int ChunkX { get; init; }
    public int ChunkY { get; init; }

    /// <summary>
    /// ProtoId grid, null = erased tile
    /// </summary>
    public ProtoId<TilePrototype>?[,] Tiles { get; init; }
    public int Size => Tiles.GetLength(0);

    /// <summary>
    /// The tilemap has been modified and the renderable chunk must be recreated.
    /// </summary>
    public bool Dirty { get; internal set; } = true;

    /// <summary>
    /// Solid tiles in this chunk, so collision queries can skip it whole.
    /// </summary>
    internal int? SolidTileCount;

    public TilemapChunk(int cx, int cy, int size)
    {
        ChunkX = cx;
        ChunkY = cy;
        Tiles = new ProtoId<TilePrototype>?[size, size];
    }
}
