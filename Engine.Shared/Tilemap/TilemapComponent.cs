using System;
using System.Collections.Generic;
using Engine.Shared.Assets;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.Timing;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Shared.Tilemap;

[RegisterComponent("Tilemap"), NetworkedComponent]
public sealed class TilemapComponent : Component
{
    /// <summary>
    /// How far back a removed chunk is remembered. A session older than this gets a full state instead.
    /// </summary>
    private readonly uint RemovalHistoryWindow = 600; // 10s at 60 t/s

    public int TileSize { get; set { field = value; DirtyBase(); } } = 128;
    public int ChunkSize { get; set { field = value; DirtyBase(); } } = 16;
    public int Layer { get; set { field = value; DirtyBase(); } } = 0;

    #region  Client-Side
    public SamplerState? SamplerState { get; set; }
    public bool TileBlending { get; set; } = true;

    [ShaderKey]
    public string? Shader { get; set { field = value; DirtyBase(); } }
    #endregion

    public Dictionary<(int, int), TilemapChunk> Chunks { get; set; } = new();

    internal GameTick BaseTick;
    private readonly List<(GameTick Tick, int X, int Y)> _removedChunks = new();
    private GameTick _removalHistoryStart = GameTick.Zero;

    private void DirtyBase()
    {
        BaseTick = EntityManager.Instance?.CurTick ?? GameTick.Zero;
        Dirty();
    }

    internal void RecordChunkRemoval(int x, int y, GameTick tick)
    {
        _removedChunks.Add((tick, x, y));

        var cutoff = tick.Value > RemovalHistoryWindow ? tick - RemovalHistoryWindow : GameTick.Zero;
        if (cutoff <= _removalHistoryStart)
            return;

        _removedChunks.RemoveAll(entry => entry.Tick < cutoff);
        _removalHistoryStart = cutoff;
    }

    public override IComponentState? GetNetState(GameTick fromTick)
    {
        // a peer further back than the removal history cannot be told what disappeared, so it gets everything
        if (fromTick == GameTick.Zero || fromTick < _removalHistoryStart)
            return BuildBase(CollectChunks(GameTick.Zero));

        return new TilemapComponentDeltaState
        {
            Base = BaseTick >= fromTick ? BuildBase([]) : null,
            Chunks = CollectChunks(fromTick),
        };
    }

    public override void HandleNetState(IComponentState state)
    {
        if (state is not TilemapComponentState s)
            return;

        TileSize = s.TileSize;
        ChunkSize = s.ChunkSize;
        Layer = s.Layer;
        Shader = s.Shader;

        foreach (var datum in s.Chunks)
            ApplyChunk(datum);

        // a full state lists every chunk there is, so anything else is gone
        foreach (var (key, _) in new List<KeyValuePair<(int, int), TilemapChunk>>(Chunks))
        {
            if (!Array.Exists(s.Chunks, d => d.X == key.Item1 && d.Y == key.Item2))
                Chunks.Remove(key);
        }
    }

    private TilemapChunkDatum[] CollectChunks(GameTick since)
    {
        var result = new List<TilemapChunkDatum>();

        foreach (var (_, chunk) in Chunks)
        {
            if (chunk.LastModifiedTick >= since)
                result.Add(Encode(chunk));
        }

        if (since != GameTick.Zero)
        {
            foreach (var (tick, x, y) in _removedChunks)
            {
                if (tick >= since)
                    result.Add(new TilemapChunkDatum { X = x, Y = y });
            }
        }

        return result.ToArray();
    }

    private TilemapComponentState BuildBase(TilemapChunkDatum[] chunks) => new()
    {
        TileSize = TileSize,
        ChunkSize = ChunkSize,
        Layer = Layer,
        Shader = Shader,
        Chunks = chunks,
    };

    /// <summary>
    /// A chunk as a palette of the tile ids it actually uses plus one byte per tile, run length encoded when that
    /// comes out smaller. Index 0 is an empty tile, so the palette starts at 1.
    /// </summary>
    private static TilemapChunkDatum Encode(TilemapChunk chunk)
    {
        var size = chunk.Size;
        var palette = new List<string>();
        var flat = new byte[size * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (chunk.Tiles[x, y] is not { } tile)
                    continue;

                var index = palette.IndexOf(tile.Id);
                if (index < 0)
                {
                    // 255 distinct tiles in one chunk would overflow this, which needs more tile prototypes than
                    // cells in the chunk
                    palette.Add(tile.Id);
                    index = palette.Count - 1;
                }

                flat[y * size + x] = (byte)(index + 1);
            }
        }

        var runs = Compress(flat);
        var rle = runs.Length < flat.Length;

        return new TilemapChunkDatum
        {
            X = chunk.ChunkX,
            Y = chunk.ChunkY,
            Palette = palette.ToArray(),
            Data = rle ? runs : flat,
            Rle = rle,
        };
    }

    private static byte[] Compress(byte[] flat)
    {
        var runs = new List<byte>();

        var i = 0;
        while (i < flat.Length)
        {
            var value = flat[i];
            var count = 1;
            while (i + count < flat.Length && flat[i + count] == value && count < byte.MaxValue)
                count++;

            runs.Add((byte)count);
            runs.Add(value);
            i += count;
        }

        return runs.ToArray();
    }

    private void ApplyChunk(TilemapChunkDatum datum)
    {
        if (datum.Data is null || datum.Palette is null)
        {
            Chunks.Remove((datum.X, datum.Y));
            return;
        }

        if (!Chunks.TryGetValue((datum.X, datum.Y), out var chunk) || chunk.Size != ChunkSize)
        {
            chunk = new TilemapChunk(datum.X, datum.Y, ChunkSize);
            Chunks[(datum.X, datum.Y)] = chunk;
        }

        var size = chunk.Size;
        var cell = 0;

        if (datum.Rle)
        {
            for (var i = 0; i + 1 < datum.Data.Length; i += 2)
            {
                for (var n = 0; n < datum.Data[i] && cell < size * size; n++)
                    Put(datum.Data[i + 1]);
            }
        }
        else
        {
            foreach (var index in datum.Data)
                Put(index);
        }

        chunk.Dirty = true;
        chunk.SolidTileCount = null;
        return;

        void Put(byte index)
        {
            if (cell >= size * size)
                return;

            chunk.Tiles[cell % size, cell / size] = index == 0
                ? (ProtoId<TilePrototype>?)null
                : new ProtoId<TilePrototype>(datum.Palette[index - 1]);

            cell++;
        }
    }
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
    internal GameTick LastModifiedTick;

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

[Serializable]
public sealed class TilemapComponentState : IComponentState
{
    public int TileSize;
    public int ChunkSize;
    public int Layer;
    public string? Shader;
    public TilemapChunkDatum[] Chunks = [];

    /// <summary>
    /// The same state with another set of chunks.
    /// </summary>
    public TilemapComponentState WithChunks(TilemapChunkDatum[] chunks) => new()
    {
        TileSize = TileSize,
        ChunkSize = ChunkSize,
        Layer = Layer,
        Shader = Shader,
        Chunks = chunks,
    };
}

[Serializable]
public sealed class TilemapComponentDeltaState : IComponentDeltaState<TilemapComponentState>
{
    public TilemapComponentState? Base;

    public TilemapChunkDatum[] Chunks = [];

    public TilemapComponentState CreateNewFullState(TilemapComponentState full)
    {
        var merged = new List<TilemapChunkDatum>(full.Chunks);

        foreach (var chunk in Chunks)
        {
            var index = merged.FindIndex(c => c.X == chunk.X && c.Y == chunk.Y);

            if (chunk.Data is null)
            {
                if (index >= 0)
                    merged.RemoveAt(index);

                continue;
            }

            if (index >= 0)
                merged[index] = chunk;
            else
                merged.Add(chunk);
        }

        return (Base ?? full).WithChunks(merged.ToArray());
    }
}

/// <summary>
/// One chunk on the wire. <see cref="Data"/> null means the chunk is gone.
/// </summary>
[Serializable]
public sealed class TilemapChunkDatum
{
    public int X;
    public int Y;

    /// <summary>
    /// The distinct tile ids this chunk uses. <see cref="Data"/> indexes it starting at 1, 0 being an empty tile.
    /// </summary>
    public string[]? Palette;

    public byte[]? Data;

    public bool Rle;
}
