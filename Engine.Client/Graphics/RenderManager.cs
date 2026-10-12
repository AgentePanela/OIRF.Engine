using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.IoC;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
    /// When set, redirects the frame's final composited output to this target instead of
    /// the backbuffer. Null (default) preserves today's exact backbuffer behavior -
    /// callers (e.g. an editor viewport panel) opt in per-frame.
    /// </summary>
    public RenderTarget2D? FinalTarget { get; set; }

    private readonly List<RenderQueue> _renderQueue = new();
    private readonly List<IPooledRenderable> _pooledEntries = new();

    // Stamped on every RenderQueue entry as it's enqueued, so same-Depth entries
    // in a layer keep their submit order instead of jumping around.
    private int _submitCounter;

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
    /// Add a IRenderable to be draw queued. Ordering against every other
    /// queued entry (shaded or unshaded) always follows Layer/Depth — whether
    /// this one bypasses lighting is resolved separately at draw time from
    /// the shader's technique name (see <see cref="RenderQueue.Unshaded"/>).
    /// </summary>
    public void Submit(IRenderable renderable, Vector2 position, Effect? shader = null)
    {
        Enqueue(new RenderQueue(renderable, position, shader, IsEffectUnshaded(shader)));
    }

    public void Submit<T>(T renderable, Vector2 position, Effect? shader = null)
        where T : struct, IRenderable
    {
        var box = RenderPool<T>.Rent();
        box.Value = renderable;
        _pooledEntries.Add(box);
        Enqueue(new RenderQueue(box, position, shader, IsEffectUnshaded(shader)));
    }

    public void Submit(RenderQueue queue)
    {
        Enqueue(queue);
    }

    /// <summary>
    /// Add an <see cref="IShapeRenderable"/> to be draw
    /// queued.
    /// </summary>
    public void SubmitShape<T>(T renderable, Vector2 position)
        where T : struct, IShapeRenderable
    {
        var box = ShapeRenderPool<T>.Rent();
        box.Value = renderable;
        _pooledEntries.Add(box);
        Enqueue(new RenderQueue(box, position, unshaded: renderable.Unshaded));
    }

    private void Enqueue(RenderQueue queue)
    {
        queue.SubmitOrder = _submitCounter++;
        _renderQueue.Add(queue);
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

    // Single draw loop for the merged queue. target/viewport must already be set.
    // Interleaves two batchers (SpriteBatch for sprites/text, ShapeBatch for debug
    // shapes).
    private void DrawRenderQueue(
        List<RenderQueue> queue,
        List<IPooledRenderable> pooled)
    {
        bool spriteOpen = false;
        bool shapeOpen = false;
        Effect? currentShader = null;
        SamplerState? currentSampler = null;
        bool currentUnshaded = false;

        // Shaders working in screen space map their pixel position to a UV,
        // and the pixel position is framebuffer relative - so they need the
        // viewport rect, not the target size: a letterboxed viewport doesn't
        // start at the origin.
        var vp = GameClient.GraphicsDevice.Viewport;
        var viewportSize = new Vector2(vp.Width, vp.Height);
        var viewportOffset = new Vector2(vp.X, vp.Y);

        queue.Sort();

        foreach (var r in queue)
        {
            if (r.Target is IShapeRenderable)
            {
                if (spriteOpen)
                {
                    End();
                    spriteOpen = false;
                    currentShader = null;
                    currentSampler = null;
                }

                if (!shapeOpen || r.Target.SamplerState != currentSampler || r.Unshaded != currentUnshaded)
                {
                    if (shapeOpen)
                        EndShapes();

                    BeginShapes(r.Target.SamplerState, DepthStencilState.None);
                    shapeOpen = true;
                    currentSampler = r.Target.SamplerState;
                    currentUnshaded = r.Unshaded;
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

                if (!spriteOpen || r.Shader != currentShader || r.Target.SamplerState != currentSampler || r.Unshaded != currentUnshaded)
                {
                    if (spriteOpen)
                        End();

                    var shaderToUse = r.Shader;

                    if (shaderToUse is not null)
                    {
                        shaderToUse.Parameters["ViewportSize"]?.SetValue(viewportSize);
                        shaderToUse.Parameters["ViewportOffset"]?.SetValue(viewportOffset);
                        shaderToUse.Parameters["Time"]?.SetValue((float)GameClient.Timing.TotalTime);

                        if (r.Target is Boxed<Sprite2D> { Value.CachedTexture: { } texture } boxed) // The size of one texel of the texture the sprite samples (its atlas page)
                        {
                            shaderToUse.Parameters["TexelSize"]?.SetValue(new Vector2(1f / texture.Width, 1f / texture.Height));

                            var region = boxed.Value.CachedRegion;
                            shaderToUse.Parameters["SpriteRegion"]?.SetValue(new Vector4(
                                (float)region.X / texture.Width, (float)region.Y / texture.Height,
                                (float)region.Width / texture.Width, (float)region.Height / texture.Height));
                        }
                    }

                    Begin(shaderToUse, r.Target.SamplerState, DepthStencilState.None);
                    spriteOpen = true;
                    currentShader = r.Shader;
                    currentSampler = r.Target.SamplerState;
                    currentUnshaded = r.Unshaded;
                }
            }

            r.Target.Draw(this, r.Position);
        }

        if (spriteOpen)
            End();
        if (shapeOpen)
            EndShapes();

        queue.Clear();

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
        // (e.g. FinalTarget) we leave the viewport alone — it's been sized
        // to match the target.
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
            return _camera.GetViewMatrix(); // world-space draws into an offscreen target
    }

    /// <summary>
    /// Prepares and begins a new sprite and text batch with the specified render state.
    /// This should be called before any draw call.
    /// <code>RenderManager.End()</code> should be called after all draw calls.
    /// </summary>
    public void Begin(Effect? effect = null, SamplerState? samplerState = null, DepthStencilState? depthStencilState = null)
    {
        var transform = ComputeTransform();

        _spriteBatch.Begin(
            samplerState: samplerState ?? DefaultSampler,
            transformMatrix: transform,
            blendState: BlendState,
            depthStencilState: depthStencilState,
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
    public void BeginShapes(SamplerState? samplerState = null, DepthStencilState? depthStencilState = null)
    {
        var transform = ComputeTransform();

        _shapeBatch.Begin(
            view: transform,
            samplerState: samplerState ?? DefaultSampler,
            blendState: BlendState,
            depthStencilState: depthStencilState);
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
        DrawRenderQueue(_renderQueue, _pooledEntries);
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

    // Shared by anything drawing a Sprite2D by key: cached atlas data if SpriteSystem already
    // resolved it once, otherwise a fresh asset manager lookup. False means the key doesn't
    // exist (yet) - callers should just skip drawing for that frame.
    private bool TryResolveSprite(Sprite2D sprite, out Texture2D texture, out Rectangle region)
    {
        if (sprite.CachedTexture is not null)
        {
            texture = sprite.CachedTexture;
            region = sprite.CachedRegion;
            return true;
        }

        if (!_asset.GetTexture(sprite.Key, out var atlasSpr, out var atlasPage))
        {
            texture = null!;
            region = default;
            return false;
        }

        texture = atlasPage.Texture;
        region = atlasSpr.Region;
        return true;
    }

    public void DrawSprite(Sprite2D sprite, Vector2 position)
    {
        if (!TryResolveSprite(sprite, out var texture, out var region))
            return;

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
            0f);
    }

    public void DrawTexture(RawTexture texture, Vector2 position)
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
            0f);
    }

    public void DrawNineSlice(NineSlice2D slice, Vector2 position)
    {
        if (!TryResolveSprite(slice.Sprite, out var texture, out var region))
            return;

        var dest = new Rectangle((int)position.X, (int)position.Y, (int)slice.Size.X, (int)slice.Size.Y);

        foreach (var patch in NineSlicePatch.Compute(region, slice.Margin, dest))
            _spriteBatch.Draw(texture, patch.Dest, patch.Source, slice.Tint);
    }
}
