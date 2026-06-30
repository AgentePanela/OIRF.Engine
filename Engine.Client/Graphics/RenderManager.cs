using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Engine.Client.Graphics;

/// <summary>
/// Manage all 2D rendering features in the engine/game.
/// </summary>
public sealed partial class RenderManager
{
    [Dependency] private readonly IAssetManager _asset = default!;
    [Dependency] private readonly ViewportAdapter _viewport = default!;
    [Dependency] private readonly Camera2D _camera = default!;

    public bool Resizing = true;
    public BlendState BlendState = BlendState.AlphaBlend;
    public SamplerState DefaultSampler => GameClient.Options.Samplimg;

    /// <summary>
    /// render queue
    /// </summary>
    private readonly SortedDictionary<int, List<RenderQueue>> _renderQueue = new();

    /// <summary>
    /// Tracks pooled wrapper instances submitted this frame
    /// </summary>
    private readonly List<IPooledRenderable> _pooledEntries = new();

    // Screen
    private SpriteBatch _spriteBatch;

    /// <summary>
    /// Controls the time that is loose trying to draw each frame.
    /// </summary>
    internal Stopwatch DrawStopwatch = new();

    public RenderManager()
    {
        IoCManager.ResolveDependencies(this);
    }

    internal void UpdateBatch(SpriteBatch batch)
    {
        _spriteBatch = batch;
        InitShapes(GameClient.GraphicsDevice);
    }

    internal void UpdateScaleMatrix()
    {
        _viewport.UpdateScaleMatrix();
    }

    /// <summary>
    /// Traslate a real position to the viewport virtual position.
    /// </summary>
    /// <param name="screenPos">Real screen position</param>
    /// <returns>Translated virtual position</returns>
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        return _camera.ScreenToWorld(screenPos);
    }

    /// <summary>
    /// Add a IRenderable to be draw queued.
    /// </summary>
    public void Submit(IRenderable renderable, Vector2 position, Effect? shader = null)
    {
        var queue = new RenderQueue(renderable, position, shader);
        Submit(queue);
    }

    /// <summary>
    /// Add a IRenderable to be draw queued.
    /// Generic submit version for struct renderables, avoids boxing via pooled wrappers.
    /// </summary>
    public void Submit<T>(T renderable, Vector2 position, Effect? shader = null)
        where T : struct, IRenderable
    {
        var box = RenderPool<T>.Rent();
        box.Value = renderable;
        _pooledEntries.Add(box);

        var queue = new RenderQueue(box, position, shader);
        Submit(queue);
    }

    /// <summary>
    /// Add a RenderQueue instance to the render queue.
    /// </summary>
    public void Submit(RenderQueue queue)
    {
        var layer = queue.Target.Layer;

        if (!_renderQueue.TryGetValue(layer, out var layerQueue))
        {
            layerQueue = new List<RenderQueue>();
            _renderQueue.Add(layer, layerQueue);
        }

        layerQueue.Add(queue);
    }

    /// <summary>
    /// Prepares and begins a new sprite and text batch with the specified render state.
    /// This should be called before any draw call.
    /// <code>RenderManager.End()</code> should be called after all draw calls.
    /// </summary>
    public void Begin(Effect? effect = null, SamplerState? samplerState = null)
    {
        if (Resizing)
        {
            var pp = GameClient.GraphicsDevice.PresentationParameters;
            int viewportX = (pp.BackBufferWidth - _viewport.VirtualWidth) / 2;
            int viewportY = (pp.BackBufferHeight - _viewport.VirtualHeight) / 2;

            GameClient.GraphicsDevice.Viewport = new Viewport(
                viewportX,
                viewportY,
                _viewport.VirtualWidth,
                _viewport.VirtualHeight
            );
        }
        else
        {
            GameClient.GraphicsDevice.Viewport = new Viewport(
                0,
                0,
                GameClient.GraphicsDevice.PresentationParameters.BackBufferWidth,
                GameClient.GraphicsDevice.PresentationParameters.BackBufferHeight
            );
        }

        Matrix transform;
        if (Resizing)
            transform = _camera.GetViewMatrix();
        else
            transform = Matrix.Identity;

        _spriteBatch.Begin(
            samplerState: samplerState ?? DefaultSampler,
            transformMatrix: transform,
            blendState: BlendState,
            effect: effect);
    }

    /// <summary>
    /// Ends the SpriteBatch that flushes all batched text and sprites to the screen.
    /// This should be called after <code>RenderManager.Start()</code>
    /// </summary>
    public void End()
    {
        _spriteBatch.End();
    }

    /// <summary>
    /// Draw all IRenderable in queue.
    /// Call this after all submit calls.
    /// </summary>
    public void DrawQueue()
    {
        DrawStopwatch.Reset();
        if (_renderQueue.Count == 0)
            return;
        
        DrawStopwatch.Start();

        Effect? currentShader = null;
        SamplerState? currentSampler = null;

        Begin(null, null);

        foreach (var (_, queue) in _renderQueue)
        {
            foreach (var r in queue)
            {
                if (r.Shader != currentShader || r.Target.SamplerState != currentSampler)
                {
                    End();
                    Begin(r.Shader, r.Target.SamplerState);
                    currentShader = r.Shader;
                    currentSampler = r.Target.SamplerState;
                }

                r.Target.Draw(this, r.Position);
            }
        }

        End();
        _renderQueue.Clear();

        // return pooled wrappers for reuse next frame
        foreach (var p in _pooledEntries)
            p.ReturnToPool();
        _pooledEntries.Clear();

        DrawStopwatch.Stop();
    }

    public void DrawRaw(AtlasPage page, AtlasSprite sprite, Vector2 position,
        Color color = default, float rotation = 0f, Vector2 origin = default, Vector2 scale = default, SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f)
    {
        _spriteBatch.Draw(
            page.Texture,
            position,
            sprite.Region,
            color,
            rotation,
            origin,
            scale,
            effects,
            layerDepth);
    }

    /// <summary>
    /// Draw a standalone Texture2D directly (not from atlas).
    /// </summary>
    public void DrawRawTexture(Texture2D texture, Vector2 position,
        Color color = default, float rotation = 0f, Vector2 origin = default, Vector2 scale = default, SpriteEffects effects = SpriteEffects.None,
        float layerDepth = 0f)
    {
        _spriteBatch.Draw(
            texture,
            position,
            null,
            color,
            rotation,
            origin,
            scale,
            effects,
            layerDepth);
    }

    public void DrawSprite(Sprite2D sprite, Vector2 position)
    {
        Texture2D texture;
        Rectangle region;

        // use cached atlas data if available (resolved once by SpriteSystem)
        if (sprite.CachedTexture is not null)
        {
            texture = sprite.CachedTexture;
            region = sprite.CachedRegion;
        }
        else
        {
            // resolve from asset manager (for sprites not cached by SpriteSystem)
            if (!_asset.GetTexture(sprite.Key, out var atlasSpr, out var atlasPage))
                return;

            texture = atlasPage.Texture;
            region = atlasSpr.Region;
        }

        region.X += (int)sprite.Offset.X;
        region.Y += (int)sprite.Offset.Y;

        _spriteBatch.Draw(
            texture,
            position,
            region,
            sprite.Color,
            sprite.Rotation,
            sprite.Origin,
            sprite.Scale,
            SpriteEffects.None,
            sprite.Depth);
    }

    public void DrawTexture(TextureRect texture, Vector2 position)
    {
        _spriteBatch.Draw(
            texture.Texture,
            position,
            texture.Region,
            texture.Color,
            texture.Rotation,
            texture.Origin,
            texture.Scale,
            SpriteEffects.None,
            texture.Depth);
    }

    public void DrawString(Label2D label, Vector2 position)
    {
        var font = ResolveFont(label);
        var styleDef = ResolveStyleDefinition(label);

        var drawColor = ResolveColor(label, styleDef);
        var drawScale = ResolveScale(label, styleDef);

        var shadowEnabled = ResolveShadowEnabled(label, styleDef);
        var shadowColor = ResolveShadowColor(label, styleDef);
        var shadowOffset = ResolveShadowOffset(label, styleDef);

        var outlineEnabled = ResolveOutlineEnabled(label, styleDef);
        var outlineColor = ResolveOutlineColor(label, styleDef);
        var outlineThickness = ResolveOutlineThickness(label, styleDef);

        if (outlineEnabled)
            DrawOutline(font, label.String ?? string.Empty, position, label, drawScale, outlineColor, outlineThickness);

        if (shadowEnabled)
            DrawShadow(font, label.String ?? string.Empty, position, label, drawScale, shadowColor, shadowOffset);

        _spriteBatch.DrawString(
            font,
            label.String ?? string.Empty,
            position,
            drawColor,
            label.Rotation,
            label.Origin,
            drawScale,
            SpriteEffects.None,
            label.Depth);
    }
}
