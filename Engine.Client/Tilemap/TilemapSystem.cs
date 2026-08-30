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
    [Dependency] private readonly RenderManager _renderMan = default!;
    [Dependency] private readonly IAssetManager _assetMan = default!;
    [Dependency] private readonly Camera2D _cam = default!;
    [Dependency] private readonly TerrainBlendingSystem _blending = default!;

    public bool FreezeDraw { get; set; } = false;

    private float _totalTime;

    // cache textures
    private Dictionary<ProtoId<TilePrototype>, (AtlasPage page, AtlasSprite spr)> _textures = new();
    private readonly Dictionary<(EntityUid Uid, int Cx, int Cy), RenderableChunk> _renderCache = new();
    private readonly Dictionary<EntityUid, (string? Name, ShaderPath Path)> _shaderCache = new();

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

    public void Draw(float dt)
    {
        var vp = GameClient.GraphicsDevice.Viewport;
        var query = GetEntitiesWithComp<TilemapComponent, TransformComponent>();
        var bounds = _cam.ViewportBounds;
        foreach ((var uid, var comp, var trans) in query)
        {
            var shader = ResolveShader(uid, comp.Shader);
            UpdateShaderParams(shader, vp);
            foreach (var chunk in comp.Chunks.Values)
                DrawChunk(uid, comp, shader, trans, chunk, bounds);
        }
    }

    private ShaderPath ResolveShader(EntityUid uid, string? name)
    {
        if (_shaderCache.TryGetValue(uid, out var cached) && cached.Name == name)
            return cached.Path;

        var path = new ShaderPath(name);
        _shaderCache[uid] = (name, path);
        return path;
    }

    private void UpdateShaderParams(ShaderPath shader, Viewport vp)
    {
        var effect = shader.Effect;
        if (effect is null)
            return;

        effect.Parameters["Time"]?.SetValue(_totalTime);
        effect.Parameters["ViewportSize"]?.SetValue(new Vector2(vp.Width, vp.Height));
        effect.Parameters["ViewportOffset"]?.SetValue(new Vector2(vp.X, vp.Y));
    }

    private void DrawChunk(EntityUid uid, TilemapComponent comp, ShaderPath shader, TransformComponent trans,
        TilemapChunk chunk, Rectangle bounds)
    {
        int worldChunkSize = comp.ChunkSize * comp.TileSize;
        float chunkWorldX = trans.Position.X + chunk.ChunkX * worldChunkSize;
        float chunkWorldY = trans.Position.Y + chunk.ChunkY * worldChunkSize;

        var chunkRect = new Rectangle((int)chunkWorldX, (int)chunkWorldY, worldChunkSize, worldChunkSize);
        if (!bounds.Intersects(chunkRect))
            return;

        var key = (uid, chunk.ChunkX, chunk.ChunkY);
        if (chunk.Dirty || !_renderCache.TryGetValue(key, out var renderable))
        {
            renderable = MakeRenderable(comp, trans, chunk);
            _renderCache[key] = renderable;
            chunk.Dirty = false;
        }

        _renderMan.Submit(renderable, Vector2.Zero, shader.Effect);
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
}
