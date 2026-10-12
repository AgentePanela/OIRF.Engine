using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.IoC;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Engine.Client.Graphics;

/// <inheritdoc cref="IRenderManager"/>
public sealed partial class RenderManager : IRenderManager
{
    [Dependency] private readonly IAssetManager _asset = default!;
    [Dependency] private readonly ViewportAdapter _viewport = default!;
    [Dependency] private readonly Camera2D _camera = default!;

    public BlendState BlendState { get; set; } = BlendState.AlphaBlend;
    public SamplerState DefaultSampler => GameClient.Options.Samplimg;

    public RenderViewport MainViewport { get; }
    public IReadOnlyList<RenderViewport> Viewports => _viewports;
    public MainViewportScaling MainViewportScaling { get; set; } = MainViewportScaling.Scaled;
    public bool PresentMainViewport { get; set; } = true;
    public RenderStats Stats { get; } = new();

    /// <summary>
    /// every viewport but the main one
    /// </summary>
    private readonly List<RenderViewport> _viewports = new();

    // The viewport being drawn right now, null outside of a frame.
    private RenderViewport? _current;

    /// <summary>
    /// What is waiting to be drawn.
    /// </summary>
    private sealed class QueueBuffer
    {
        public readonly List<RenderQueue> Entries = new();
        public readonly List<IPooledRenderable> Pooled = new();
    }

    private QueueBuffer _queue = new();

    /// <summary>
    /// holds what was submitted outside of a viewport draw while the other viewports are drawn
    /// </summary>
    private QueueBuffer _mainQueue = new();

    // Stamped on every RenderQueue entry as it's enqueued, so same-Depth entries
    // in a layer keep their submit order instead of jumping around.
    private int _submitCounter;

    // counts for the frame being drawn, copied to Stats once it is done
    private readonly RenderStats _frameStats = new();

    // Screen
    private SpriteBatch _spriteBatch;
    private ShapeBatch _shapeBatch;

    public RenderManager()
    {
        IoCManager.ResolveDependencies(this);

        MainViewport = new RenderViewport("Main", _camera);
        MainViewport.Resize(new Vector2(GameClient.Options.Width, GameClient.Options.Height));
    }

    internal void Init(SpriteBatch batch, ShapeBatch shapeBatch)
    {
        _spriteBatch = batch;
        _shapeBatch = shapeBatch;
        SyncMainViewport();
    }

    #region Queue

    public void Submit(IRenderable renderable, Vector2 position, Effect? shader = null)
    {
        Enqueue(new RenderQueue(renderable, position, shader, IsEffectUnshaded(shader)));
    }

    public void Submit<T>(T renderable, Vector2 position, Effect? shader = null)
        where T : struct, IRenderable
    {
        var box = RenderPool<T>.Rent();
        box.Value = renderable;
        _queue.Pooled.Add(box);
        Enqueue(new RenderQueue(box, position, shader, IsEffectUnshaded(shader)));
    }

    public void Submit(RenderQueue queue)
    {
        Enqueue(queue);
    }

    public void SubmitShape<T>(T renderable, Vector2 position)
        where T : struct, IShapeRenderable
    {
        var box = ShapeRenderPool<T>.Rent();
        box.Value = renderable;
        _queue.Pooled.Add(box);
        Enqueue(new RenderQueue(box, position, unshaded: renderable.Unshaded));
    }

    private void Enqueue(RenderQueue queue)
    {
        queue.SubmitOrder = _submitCounter++;
        _queue.Entries.Add(queue);
        _frameStats.Submitted++;
    }

    // Draws and empties the queue into the target that is set.
    private void DrawQueue()
    {
        _submitCounter = 0; // reset per draw so SubmitOrder doesnt climb toward int overflow

        if (_queue.Entries.Count == 0)
            return;

        var sortStart = Stopwatch.GetTimestamp();
        _queue.Entries.Sort();
        _frameStats.SortMs += Stopwatch.GetElapsedTime(sortStart).TotalMilliseconds;

        var flushStart = Stopwatch.GetTimestamp();
        DrawRenderQueue(_queue.Entries, _queue.Pooled);

        _frameStats.FlushMs += Stopwatch.GetElapsedTime(flushStart).TotalMilliseconds;
    }

    // Single draw loop for the sorted queue. target/viewport must already be set.
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

        // for shaders working in screen space
        var vp = GameClient.GraphicsDevice.Viewport;
        var viewportSize = new Vector2(vp.Width, vp.Height);

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
                    _frameStats.BreaksByKind++;
                }

                if (!shapeOpen || r.Target.SamplerState != currentSampler || r.Unshaded != currentUnshaded)
                {
                    if (shapeOpen)
                    {
                        EndShapes();
                        if (r.Target.SamplerState != currentSampler)
                            _frameStats.BreaksBySampler++;
                        else
                            _frameStats.BreaksByUnshaded++;
                    }

                    BeginShapes(r.Target.SamplerState, DepthStencilState.None);

                    _frameStats.ShapeBatches++;
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
                    _frameStats.BreaksByKind++;
                }

                if (!spriteOpen || r.Shader != currentShader || r.Target.SamplerState != currentSampler || r.Unshaded != currentUnshaded)
                {
                    if (spriteOpen)
                    {
                        End();
                        if (r.Shader != currentShader)
                            _frameStats.BreaksByShader++;
                        else if (r.Target.SamplerState != currentSampler)
                            _frameStats.BreaksBySampler++;
                        else
                            _frameStats.BreaksByUnshaded++;
                    }

                    var shaderToUse = r.Shader;

                    if (shaderToUse is not null)
                    {
                        shaderToUse.Parameters["ViewportSize"]?.SetValue(viewportSize);
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
                    _frameStats.SpriteBatches++;
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

    public void End()
    {
        _spriteBatch.End();
    }

    public void BeginShapes(SamplerState? samplerState = null, DepthStencilState? depthStencilState = null)
    {
        var transform = ComputeTransform();

        _shapeBatch.Begin(
            view: transform,
            samplerState: samplerState ?? DefaultSampler,
            blendState: BlendState,
            depthStencilState: depthStencilState);
    }

    public void EndShapes()
    {
        _shapeBatch.End();
    }
}
