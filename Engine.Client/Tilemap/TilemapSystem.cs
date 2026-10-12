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
using Engine.Shared.Tilemap;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Tilemap;

/// <inheritdoc cref="SharedTilemapSystem"/>
public sealed class TilemapSystem : SharedTilemapSystem, IEntityDrawSystem
{
    [Dependency] private readonly IRenderManager _renderMan = default!;
    [Dependency] private readonly IAssetManager _assetMan = default!;
    [Dependency] private readonly ShaderManager _shaderMan = default!;
    [Dependency] private readonly TerrainBlendingSystem _blending = default!;

    public bool FreezeDraw { get; set; } = false;

    // cache textures
    private Dictionary<ProtoId<TilePrototype>, (AtlasPage page, AtlasSprite spr)> _textures = new();
    private readonly Dictionary<(EntityUid Uid, int Cx, int Cy), RenderableChunk> _renderCache = new();

    /// <summary>
    /// One thing a chunk draws: a tile, or the blend overlay that goes over it.
    /// </summary>
    public struct ChunkQuad
    {
        public Texture2D Texture;
        public Rectangle Source;
        public Vector2 Position;
    }

    /// <summary>
    /// Represents the renderable chunk piece, this replaces creating a sprite 2D per tile, so render manager does not get a ton of sprites
    /// to have in queue.
    /// </summary>
    public struct RenderableChunk : IRenderable
    {
        public ChunkQuad[] Quads;

        public int Layer { get; set; }
        public float Depth { get; set; }
        public SamplerState? SamplerState { get; set; }

        public void Draw(IRenderManager renderer, Vector2 pos)
        {
            foreach (ref readonly var quad in Quads.AsSpan())
                renderer.DrawQuad(quad.Texture, quad.Source, quad.Position, Color.White);
        }
    }

    public override void Init()
    {
        base.Init();
        SubscribeEvent<LoadingFinishedEvent>(OnLoaded); //! change this if we do prototype hot reload some day
        SubscribeEvent<TilemapComponent, CompRemovedEvent>(OnTilemapRemoved);
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

    // the chunks of a tilemap that is gone would stay baked forever otherwise
    private void OnTilemapRemoved(EntityUid uid, TilemapComponent comp, CompRemovedEvent args)
    {
        foreach (var chunk in comp.Chunks.Values)
            _renderCache.Remove((uid, chunk.ChunkX, chunk.ChunkY));
    }

    public void Draw(in DrawContext ctx)
    {
        var query = GetEntitiesWithComp<TilemapComponent, TransformComponent>();
        foreach ((var uid, var comp, var trans) in query)
        {
            var shader = _shaderMan.GetSharedInstance(comp.Shader);
            foreach (var chunk in comp.Chunks.Values)
                DrawChunk(ctx.Camera, uid, comp, shader, trans, chunk);
        }
    }

    private void DrawChunk(Camera2D camera, EntityUid uid, TilemapComponent comp, ShaderInstance? shader, TransformComponent trans,
        TilemapChunk chunk)
    {
        int worldChunkSize = comp.ChunkSize * comp.TileSize;
        float chunkWorldX = trans.Position.X + chunk.ChunkX * worldChunkSize;
        float chunkWorldY = trans.Position.Y + chunk.ChunkY * worldChunkSize;

        if (!camera.IsOnScreen(new Vector2(chunkWorldX, chunkWorldY), new Vector2(worldChunkSize)))
        {
            _renderMan.ReportCulled();
            return;
        }

        var key = (uid, chunk.ChunkX, chunk.ChunkY);
        if (chunk.Dirty || !_renderCache.TryGetValue(key, out var renderable))
        {
            renderable = MakeRenderable(comp, trans, chunk);
            _renderCache[key] = renderable;
            chunk.Dirty = false;
        }

        _renderMan.Submit(renderable, Vector2.Zero, shader);
    }

    private RenderableChunk MakeRenderable(TilemapComponent comp, TransformComponent trans, TilemapChunk chunk)
    {
        var size = chunk.Size;
        var quads = new List<ChunkQuad>(size * size);

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

                if (!_textures.TryGetValue(tileId.Value, out var tex))
                    continue;

                var position = new Vector2(
                    chunkWorldX + x * comp.TileSize,
                    chunkWorldY + y * comp.TileSize);

                quads.Add(new ChunkQuad { Texture = tex.page.Texture, Source = tex.spr.Region, Position = position });

                // the blend overlay goes right over its tile
                if (comp.TileBlending
                    && _blending.GetBlendOverlay(comp, worldTileStartX + x, worldTileStartY + y, comp.TileSize) is { } overlay)
                    quads.Add(new ChunkQuad { Texture = overlay, Source = overlay.Bounds, Position = position });
            }
        }

        return new RenderableChunk
        {
            Quads = quads.ToArray(),
            Layer = comp.Layer,
            SamplerState = comp.SamplerState
        };
    }
}
