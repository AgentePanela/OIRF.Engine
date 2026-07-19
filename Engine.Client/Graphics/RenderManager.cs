using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Graphics.Lighting;
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
    [Dependency] private readonly LightingManager _lighting = default!;

    public bool Resizing = true;
    public BlendState BlendState = BlendState.AlphaBlend;
    public SamplerState DefaultSampler => GameClient.Options.Samplimg;

    /// <summary>
    /// When set, redirects the frame's final composited output to this target instead of
    /// the backbuffer. Null (default) preserves today's exact backbuffer behavior -
    /// callers (e.g. an editor viewport panel) opt in per-frame.
    /// </summary>
    public RenderTarget2D? FinalTarget { get; set; }

    private readonly SortedDictionary<int, List<RenderQueue>> _renderQueue = new();
    private readonly List<IPooledRenderable> _pooledEntries = new();

    // Sprites whose Effect carries IsUnshaded=true are diverted here and
    // drawn after ApplyAfterWorld, so they appear at full brightness on top
    // of the composed lightmap. When lighting is disabled they fall through
    // to the regular _renderQueue so layer ordering is preserved.
    private readonly SortedDictionary<int, List<RenderQueue>> _unshadedQueue = new();
    private readonly List<IPooledRenderable> _unshadedPooledEntries = new();

    // Stamped on every RenderQueue entry as it's enqueued, so same-Depth entries
    // in a layer keep their submit order instead of jumping around.
    private int _submitCounter;
    private static readonly Comparison<RenderQueue> DepthComparison = (a, b) =>
    {
        var cmp = a.Target.Depth.CompareTo(b.Target.Depth);
        return cmp != 0 ? cmp : a.SubmitOrder.CompareTo(b.SubmitOrder);
    };

    // Screen
    private SpriteBatch _spriteBatch;
    private ShapeBatch _shapeBatch;

    /// <summary>
    /// Controls the time that is loose trying to draw each frame.
    /// </summary>
    internal Stopwatch DrawStopwatch = new();

    public RenderManager()
    {
        IoCManager.ResolveDependencies(this);
    }

    internal void UpdateBatch(SpriteBatch batch, ShapeBatch shapeBatch)
    {
        _spriteBatch = batch;
        _shapeBatch = shapeBatch;
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
    /// If the shader declares IsUnshaded=true, or the renderable itself opts in via
    /// <see cref="IRenderable.Unshaded"/>, and lighting is active, the renderable is
    /// automatically diverted to the unshaded queue and will draw on top of the
    /// lightmap at full brightness.
    /// </summary>
    public void Submit(IRenderable renderable, Vector2 position, Effect? shader = null)
    {
        var queue = new RenderQueue(renderable, position, shader);
        if (_lighting?.Enabled == true && (IsEffectUnshaded(shader) || renderable.Unshaded))
            EnqueueUnshaded(queue);
        else
            Enqueue(queue);
    }

    public void Submit<T>(T renderable, Vector2 position, Effect? shader = null)
        where T : struct, IRenderable
    {
        if (_lighting?.Enabled == true && (IsEffectUnshaded(shader) || renderable.Unshaded))
        {
            var box = RenderPool<T>.Rent();
            box.Value = renderable;
            _unshadedPooledEntries.Add(box);
            EnqueueUnshaded(new RenderQueue(box, position, shader));
        }
        else
        {
            var box = RenderPool<T>.Rent();
            box.Value = renderable;
            _pooledEntries.Add(box);
            Enqueue(new RenderQueue(box, position, shader));
        }
    }

    public void Submit(RenderQueue queue)
    {
        if (_lighting?.Enabled == true && (IsEffectUnshaded(queue.Shader) || queue.Target.Unshaded))
            EnqueueUnshaded(queue);
        else
            Enqueue(queue);
    }

    private void Enqueue(RenderQueue queue)
    {
        var layer = queue.Target.Layer;
        if (!_renderQueue.TryGetValue(layer, out var layerQueue))
        {
            layerQueue = new List<RenderQueue>();
            _renderQueue.Add(layer, layerQueue);
        }
        queue.SubmitOrder = _submitCounter++;
        layerQueue.Add(queue);
    }

    private void EnqueueUnshaded(RenderQueue queue)
    {
        var layer = queue.Target.Layer;
        if (!_unshadedQueue.TryGetValue(layer, out var layerQueue))
        {
            layerQueue = new List<RenderQueue>();
            _unshadedQueue.Add(layer, layerQueue);
        }
        queue.SubmitOrder = _submitCounter++;
        layerQueue.Add(queue);
    }

    // True when the effect's active technique is named "Unshaded".
    // Technique names survive MGFX compilation on all backends (unlike bool
    // parameter default values, which GLSL/DesktopGL may not preserve).
    private static bool IsEffectUnshaded(Effect? effect) =>
        effect?.CurrentTechnique?.Name == "Unshaded";

    /// <summary>
    /// Sets a plain full-size viewport (no letterboxing) — used when rendering into a
    /// custom-sized <see cref="FinalTarget"/> instead of the backbuffer.
    /// </summary>
    public void SetFullViewport(int width, int height)
        => GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, width, height);

    public void DrawUnshadedQueue()
    {
        if (_unshadedQueue.Count == 0)
            return;

        GameClient.GraphicsDevice.SetRenderTarget(FinalTarget);
        GameClient.GraphicsDevice.Viewport = FinalTarget is not null
            ? new Viewport(0, 0, FinalTarget.Width, FinalTarget.Height)
            : LastBackbufferViewport;

        DrawRenderQueue(_unshadedQueue, _unshadedPooledEntries);
    }

    // shared draw loop for the lit and unshaded queues. target/viewport must already be set.
    // Interleaves two batchers (SpriteBatch for sprites/text, ShapeBatch for debug
    // shapes)
    private void DrawRenderQueue(
        SortedDictionary<int, List<RenderQueue>> queues,
        List<IPooledRenderable> pooled)
    {
        bool spriteOpen = false;
        bool shapeOpen = false;
        Effect? currentShader = null;
        SamplerState? currentSampler = null;

        foreach (var (_, queue) in queues)
        {
            queue.Sort(DepthComparison);
            foreach (var r in queue)
            {
                if (r.Target.UsesShapeBatch)
                {
                    if (spriteOpen)
                    {
                        End();
                        spriteOpen = false;
                        currentShader = null;
                        currentSampler = null;
                    }

                    if (!shapeOpen || r.Target.SamplerState != currentSampler)
                    {
                        if (shapeOpen)
                            EndShapes();

                        BeginShapes(r.Target.SamplerState);
                        shapeOpen = true;
                        currentSampler = r.Target.SamplerState;
                    }
                }
                else
                {
                    if (shapeOpen)
                    {
                        EndShapes();
                        shapeOpen = false;
                        currentSampler = null;
                    }

                    if (!spriteOpen || r.Shader != currentShader || r.Target.SamplerState != currentSampler)
                    {
                        if (spriteOpen)
                            End();

                        Begin(r.Shader, r.Target.SamplerState);
                        spriteOpen = true;
                        currentShader = r.Shader;
                        currentSampler = r.Target.SamplerState;
                    }
                }

                r.Target.Draw(this, r.Position);
            }
        }

        if (spriteOpen)
            End();
        if (shapeOpen)
            EndShapes();

        queues.Clear();

        foreach (var p in pooled)
            p.ReturnToPool();
        pooled.Clear();
    }

    // Shared viewport/transform-matrix setup used by both Begin() (SpriteBatch)
    // and BeginShapes() (ShapeBatch)
    private Matrix ComputeTransform()
    {
        // Only force a viewport swap when we're rendering directly to the
        // backbuffer. When the caller has already configured a target
        // (e.g. the lighting SceneTarget during DrawQueue) we leave the
        // viewport alone — it's been sized to match the target.
        bool isOnBackbuffer = GameClient.GraphicsDevice.GetRenderTargets().Length == 0
                              || GameClient.GraphicsDevice.GetRenderTargets()[0].RenderTarget is null;

        if (isOnBackbuffer && Resizing)
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
        else if (isOnBackbuffer)
        {
            GameClient.GraphicsDevice.Viewport = new Viewport(
                0,
                0,
                GameClient.GraphicsDevice.PresentationParameters.BackBufferWidth,
                GameClient.GraphicsDevice.PresentationParameters.BackBufferHeight
            );
        }

        if (Resizing && isOnBackbuffer)
            return _camera.GetViewMatrix();
        else if (isOnBackbuffer)
            return Matrix.Identity;
        else
            return _camera.GetViewMatrix(); // world-space draws into SceneTarget
    }

    /// <summary>
    /// Prepares and begins a new sprite and text batch with the specified render state.
    /// This should be called before any draw call.
    /// <code>RenderManager.End()</code> should be called after all draw calls.
    /// </summary>
    public void Begin(Effect? effect = null, SamplerState? samplerState = null)
    {
        var transform = ComputeTransform();

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
    /// Prepares and begins a new Apos.Shapes batch for debug/gizmo shapes.
    /// <see cref="EndShapes"/> should be called after all shape draw calls.
    /// </summary>
    public void BeginShapes(SamplerState? samplerState = null)
    {
        var transform = ComputeTransform();

        _shapeBatch.Begin(
            view: transform,
            samplerState: samplerState ?? DefaultSampler,
            blendState: BlendState);
    }

    /// <summary>
    /// Ends the ShapeBatch and draw in the screen.
    /// </summary>
    public void EndShapes()
    {
        _shapeBatch.End();
    }

    /// <summary>
    /// Sets the backbuffer viewport to the centered, scaled-virtual-size rect
    /// used for letterboxing/pillarboxing. Mirrors the on-backbuffer +
    /// Resizing branch of <see cref="Begin"/> without opening a SpriteBatch.
    /// Call this at the top of a frame (before any draw that may capture
    /// <see cref="LastBackbufferViewport"/>) so the lighting apply pass blits
    /// the SceneTarget at the correct aspect ratio.
    /// </summary>
    public void SetLetterboxedBackbufferViewport()
    {
        var pp = GameClient.GraphicsDevice.PresentationParameters;
        int viewportX = (pp.BackBufferWidth - _viewport.VirtualWidth) / 2;
        int viewportY = (pp.BackBufferHeight - _viewport.VirtualHeight) / 2;
        GameClient.GraphicsDevice.Viewport = new Viewport(
            viewportX,
            viewportY,
            _viewport.VirtualWidth,
            _viewport.VirtualHeight);
    }

    /// <summary>
    /// Draw all IRenderable in queue.
    /// Call this after all submit calls.
    /// </summary>
    public void DrawQueue()
    {
        _submitCounter = 0; // reset per frame so SubmitOrder doesnt climb toward int overflow
        DrawStopwatch.Reset();
        if (_renderQueue.Count == 0)
            return;

        DrawStopwatch.Start();

        // When the lighting system is active, draw the world into the
        // offscreen SceneTarget so the lighting pass can sample it. The
        // final composite (apply pass + blit) is performed separately by
        // GameClient.Draw after this returns.
        bool lightingActive = _lighting?.Enabled ?? false;
        Viewport previousViewport = default;
        bool didSwitchTarget = false;

        if (lightingActive && SceneTarget is not null)
        {
            // Recompute the backbuffer viewport directly — do NOT read
            // GraphicsDevice.Viewport here. LightingSystem.Draw() sets
            // per-light row viewports during shadow-map rendering and never
            // restores them, so the device viewport is stale by this point.
            if (FinalTarget is not null)
            {
                previousViewport = new Viewport(0, 0, FinalTarget.Width, FinalTarget.Height);
            }
            else if (Resizing)
            {
                var pp = GameClient.GraphicsDevice.PresentationParameters;
                int vx = (pp.BackBufferWidth  - _viewport.VirtualWidth)  / 2;
                int vy = (pp.BackBufferHeight - _viewport.VirtualHeight) / 2;
                previousViewport = new Viewport(vx, vy, _viewport.VirtualWidth, _viewport.VirtualHeight);
            }
            else
            {
                var pp = GameClient.GraphicsDevice.PresentationParameters;
                previousViewport = new Viewport(0, 0, pp.BackBufferWidth, pp.BackBufferHeight);
            }

            GameClient.GraphicsDevice.SetRenderTarget(SceneTarget);
            GameClient.GraphicsDevice.Clear(Color.Transparent);
            GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, SceneTarget.Width, SceneTarget.Height);
            didSwitchTarget = true;
        }

        DrawRenderQueue(_renderQueue, _pooledEntries);

        if (didSwitchTarget)
        {
            GameClient.GraphicsDevice.SetRenderTarget(FinalTarget);
            GameClient.GraphicsDevice.Viewport = previousViewport;
        }

        // Cache the final backbuffer viewport so the lighting apply pass
        // can use the same letterboxed layout.
        LastBackbufferViewport = GameClient.GraphicsDevice.Viewport;

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
            sprite.Effects,
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
