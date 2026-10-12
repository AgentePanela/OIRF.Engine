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
    /// the backbuffer.
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

    internal void Init(SpriteBatch batch, ShapeBatch shapeBatch)
    {
        _spriteBatch = batch;
        _shapeBatch = shapeBatch;
    }

    #region Matrix

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
    /// Sets a plain full-size viewport (no letterboxing)
    /// </summary>
    public void SetFullViewport(int width, int height)
        => GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, width, height);

    internal void UpdateScaleMatrix()
    {
        _viewport.UpdateScaleMatrix();
    }

    // Shared viewport/transform-matrix setup used by both Begin() (SpriteBatch)
    // and BeginShapes() (ShapeBatch)
    private Matrix ComputeTransform()
    {
        // Only force a viewport swap when we're rendering directly to the backbuffer.
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
    /// Sets the backbuffer viewport to the centered, scaled-virtual-size rect
    /// used for letterboxing/pillarboxing.
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
    #endregion

    #region Queue

    /// <summary>
    /// Add a IRenderable to be draw queued.
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
    /// Add an <see cref="IShapeRenderable"/> to be draw queued.
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

    #endregion

    /// <summary>
    /// Prepares and begins a new sprite and text batch with the specified render state.
    /// This should be called before any draw call.
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
    /// Prepares and begins a new Apos.Shapes batch for shapes.
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
}
