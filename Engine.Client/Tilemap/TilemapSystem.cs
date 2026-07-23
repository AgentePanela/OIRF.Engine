using System;
using System.Collections.Generic;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Shared.GameObjects;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.Prototypes;
using Engine.Client.Scenes;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Tilemap;

public sealed class TilemapSystem : EntityDrawSystem
{
    [Dependency] private readonly RenderManager _renderMan = default!;
    [Dependency] private readonly IAssetManager _assetMan = default!;
    [Dependency] private readonly Camera2D _cam = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly TerrainBlendingSystem _blending = default!;

    private float _totalTime;
    
    // cache textures
    private Dictionary<ProtoId<TilePrototype>, (AtlasPage page, AtlasSprite spr)> _textures = new();

    /// <summary>
    /// Represents the renderable chunk piece, this replaces creating a sprite 2D per tile, so render manager does not get a ton of sprites
    /// to have in queue.
    /// </summary>
    public struct RenderableChunk : IRenderable
    {
        private static TilemapSystem _system => IoCManager.Resolve<TilemapSystem>();

        public ProtoId<TilePrototype>?[,] Tiles;
        public Vector2?[,] Pos;
        public Texture2D?[,] BlendOverlays;

        public int Layer { get; set; }
        public float Depth { get; set; }
        public SamplerState? SamplerState { get; set; }

        public void Draw(RenderManager renderer, Vector2 pos)
        {
            for (int x = 0; x < Tiles.GetLength(0); x++)
            for (int y = 0; y < Tiles.GetLength(1); y++)
            {
                var tile = Tiles[x, y];
                var tpos = Pos[x, y];
                if (tile is null || tpos is null ||
                    !_system._textures.TryGetValue(tile.Value, out var tex))
                    continue;
                
                renderer.DrawRaw(tex.page, tex.spr, tpos.Value, Color.White, scale: Vector2.One);

                // draw blend overlay on top ih have one
                var overlay = BlendOverlays[x, y];
                if (overlay is not null)
                    renderer.DrawRawTexture(overlay, tpos.Value, Color.White, scale: Vector2.One);
            }
        }
    }

    public override void Init()
    {
        base.Init();
        SubscribeEvent<LoadingFinishedEvent>(OnLoaded); //! change this if we do prototype hot reload some day
    }

    private void OnLoaded(LoadingFinishedEvent args)
    {
        var protos = _proto.GetAll<TilePrototype>();
        foreach ((_, var proto) in protos)
        {
            if (_textures.ContainsKey(proto.ID))
                continue;
            
            if (!_assetMan.GetTexture(proto.Sprite, out var spr, out var page))
                continue;

            _textures.Add(proto.ID, (page, spr));
        }
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        _totalTime += dt;
    }

    public override void Draw(float dt)
    {
        base.Draw(dt);

        var vp = GameClient.GraphicsDevice.Viewport;
        var query = GetEntitiesWithComp<TilemapComponent, TransformComponent>();
        var bounds = _cam.ViewportBounds;
        foreach ((_, var comp, var trans) in query)
        {
            UpdateShaderParams(comp, vp);
            foreach (var chunk in comp.Chunks.Values)
                DrawChunk(comp, trans, chunk, bounds);
        }
    }

    private void UpdateShaderParams(TilemapComponent comp, Viewport vp)
    {
        var effect = comp.Shader.Effect;
        if (effect is null)
            return;

        effect.Parameters["Time"]?.SetValue(_totalTime);
        effect.Parameters["ViewportSize"]?.SetValue(new Vector2(vp.Width, vp.Height));
    }

    private void DrawChunk(TilemapComponent comp, TransformComponent trans,
        TilemapChunk chunk, Rectangle bounds)
    {
        int worldChunkSize = comp.ChunkSize * comp.TileSize;
        float chunkWorldX = trans.Position.X + chunk.ChunkX * worldChunkSize;
        float chunkWorldY = trans.Position.Y + chunk.ChunkY * worldChunkSize;

        var chunkRect = new Rectangle((int)chunkWorldX, (int)chunkWorldY, worldChunkSize, worldChunkSize);
        if (!bounds.Intersects(chunkRect))
            return;

        if (chunk.Dirty || chunk.CachedRenderable is null)
        {
            chunk.CachedRenderable = MakeRenderable(comp, trans, chunk);
            chunk.Dirty = false;
        }

        _renderMan.Submit(chunk.CachedRenderable.Value, Vector2.Zero, comp.Shader.Effect);
    }

    private RenderableChunk MakeRenderable(TilemapComponent comp, TransformComponent trans, TilemapChunk chunk)
    {
        var size = chunk.Size;

        var tiles = new ProtoId<TilePrototype>?[size, size];
        var positions = new Vector2?[size, size];
        var blendOverlays = new Texture2D?[size, size];

        int worldChunkSize = comp.ChunkSize * comp.TileSize;

        float chunkWorldX = trans.Position.X + chunk.ChunkX * worldChunkSize;
        float chunkWorldY = trans.Position.Y + chunk.ChunkY * worldChunkSize;

        int worldTileStartX = chunk.ChunkX * comp.ChunkSize;
        int worldTileStartY = chunk.ChunkY * comp.ChunkSize;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                var tileId = chunk.Tiles[x, y];
                if (tileId is null)
                    continue;

                if (!_textures.ContainsKey(tileId.Value))
                    continue;

                tiles[x, y] = tileId;

                positions[x, y] = new Vector2(
                    chunkWorldX + x * comp.TileSize,
                    chunkWorldY + y * comp.TileSize);

                // compute blend overlay for this tile
                int worldTileX = worldTileStartX + x;
                int worldTileY = worldTileStartY + y;
                if (comp.TileBlending)
                    blendOverlays[x, y] = _blending.GetBlendOverlay(comp, worldTileX, worldTileY, comp.TileSize);
            }
        }

        return new RenderableChunk
        {
            Tiles = tiles,
            Pos = positions,
            BlendOverlays = blendOverlays,
            Layer = comp.Layer,
            SamplerState = comp.SamplerState
        };
    }

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
        {
            chunk.CachedRenderable = null;
            chunk.Dirty = true;
        }

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
        Rectangle area, List<Rectangle> results)
    {
        results.Clear();

        int tileSize = comp.TileSize;
        float originX = tilemapTransform.Position.X;
        float originY = tilemapTransform.Position.Y;

        // convert area to tile cordinates
        int tileMinX = (int)Math.Floor((area.Left - originX) / (float)tileSize);
        int tileMinY = (int)Math.Floor((area.Top - originY) / (float)tileSize);
        int tileMaxX = (int)Math.Floor((area.Right - originX) / (float)tileSize);
        int tileMaxY = (int)Math.Floor((area.Bottom - originY) / (float)tileSize);

        for (int ty = tileMinY; ty <= tileMaxY; ty++)
        {
            for (int tx = tileMinX; tx <= tileMaxX; tx++)
            {
                if (!IsTileSolid(comp, tx, ty))
                    continue;

                results.Add(new Rectangle(
                    (int)(originX + tx * tileSize),
                    (int)(originY + ty * tileSize),
                    tileSize,
                    tileSize));
            }
        }
    }
}
