using System.Collections.Generic;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework.Graphics;
using static Engine.Client.Tilemap.TilemapSystem;

namespace Engine.Client.Tilemap;

[RegisterComponent("Tilemap")]
public sealed class TilemapComponent : Component
{
    public int TileSize { get; set; } = 128;
    public int ChunkSize { get; set; } = 16;
    public int Layer { get; set; } = 0;
    public SamplerState? SamplerState { get; set; }
    public bool TileBlending { get; set; } = true;

    public ShaderPath Shader { get; set; }

    public Dictionary<(int, int), TilemapChunk> Chunks { get; } = new();
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
    internal RenderableChunk? CachedRenderable;

    public TilemapChunk(int cx, int cy, int size)
    {
        ChunkX = cx;
        ChunkY = cy;
        Tiles = new ProtoId<TilePrototype>?[size, size];
    }
}
