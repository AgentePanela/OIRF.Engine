using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Client.Graphics.Batching;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.IoC;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Engine.Client.Graphics;

/// <inheritdoc cref="IRenderManager"/>
public sealed partial class RenderManager : IRenderManager
{
    [Dependency] private readonly IAssetManager _asset = default!;
    [Dependency] private readonly ViewportAdapter _viewport = default!;
    [Dependency] private readonly Camera2D _camera = default!;
    [Dependency] private readonly ShaderManager _shaders = default!;

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
    private DrawCommandBuffer _queue = new();

    /// <summary>
    /// holds what was submitted outside of a viewport draw while the other viewports are drawn
    /// </summary>
    private DrawCommandBuffer _mainQueue = new();

    // counts for the frame being drawn, copied to Stats once it is done
    private readonly RenderStats _frameStats = new();

    // Screen
    private SpriteBatch _spriteBatch;
    private ShapeBatch _shapeBatch;
    private SpriteBatcher _batcher;

    // The sampler of the command being drawn, what an IRenderable drawing itself goes back to after text.
    private SamplerState _commandSampler = SamplerState.PointClamp;

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
        _batcher = new SpriteBatcher(GameClient.GraphicsDevice, _frameStats);
        _textRenderer = new TextRenderer(GameClient.GraphicsDevice, _batcher);
        SyncMainViewport();
    }

    #region Queue

    public void Submit(Sprite2D sprite, Vector2 position, ShaderInstance? shader = null)
        => Submit(sprite, position, shader, default, default);

    public void Submit(Sprite2D sprite, Vector2 position, ShaderInstance? shader, Vector4 custom0, Vector4 custom1 = default)
    {
        if (!TryResolveSprite(sprite, out var texture, out var region))
            return;

        region.X += (int)sprite.Offset.X;
        region.Y += (int)sprite.Offset.Y;

        ref var command = ref _queue.Add(sprite.Layer, sprite.Depth);
        command.Kind = DrawCommandType.Sprite;
        command.Lit = shader?.Lit ?? true;
        command.Shader = shader;
        command.Sampler = sprite.SamplerState;
        command.Position = position;
        command.Texture = texture;
        command.Source = region;
        command.Color = sprite.Color;
        command.Rotation = sprite.Rotation;
        command.Origin = sprite.Origin;
        command.Scale = sprite.Scale;
        command.Effects = sprite.Effects;
        command.Custom0 = custom0;
        command.Custom1 = custom1;
        StampMask(ref command);
        _frameStats.Submitted++;
    }

    public void Submit(RawTexture texture, Vector2 position, ShaderInstance? shader = null)
        => Submit(texture, position, shader, default, default);

    public void Submit(RawTexture texture, Vector2 position, ShaderInstance? shader, Vector4 custom0, Vector4 custom1 = default)
    {
        ref var command = ref _queue.Add(texture.Layer, texture.Depth);
        command.Kind = DrawCommandType.Sprite;
        command.Lit = shader?.Lit ?? true;
        command.Shader = shader;
        command.Sampler = texture.SamplerState;
        command.Position = position;
        command.Texture = texture.Texture;
        command.Source = texture.Region;
        command.Color = texture.Color;
        command.Rotation = texture.Rotation;
        command.Origin = texture.Origin;
        command.Scale = texture.Scale;
        command.Custom0 = custom0;
        command.Custom1 = custom1;
        StampMask(ref command);
        _frameStats.Submitted++;
    }

    public void Submit(IRenderable renderable, Vector2 position, ShaderInstance? shader = null)
    {
        ref var command = ref _queue.Add(renderable.Layer, renderable.Depth);
        command.Target = renderable;
        command.Position = position;
        command.Sampler = renderable.SamplerState;

        if (renderable is IShapeRenderable shape)
        {
            // shapes cannot hold a shader
            command.Kind = DrawCommandType.Shape;
            command.Lit = !shape.Unshaded;
        }
        else
        {
            command.Kind = DrawCommandType.Renderable;
            command.Shader = shader;
            command.Lit = shader?.Lit ?? true;
        }

        StampMask(ref command);
        _frameStats.Submitted++;
    }

    public void Submit<T>(T renderable, Vector2 position, ShaderInstance? shader = null)
        where T : struct, IRenderable
    {
        // a sprite is plain data, it needs no box
        if (typeof(T) == typeof(Sprite2D))
        {
            Submit(Unsafe.As<T, Sprite2D>(ref renderable), position, shader);
            return;
        }

        if (typeof(T) == typeof(RawTexture))
        {
            Submit(Unsafe.As<T, RawTexture>(ref renderable), position, shader);
            return;
        }

        var box = RenderPool<T>.Rent();
        box.Value = renderable;
        _queue.Pooled.Add(box);
        Submit((IRenderable)box, position, shader);
    }

    public void Submit(RenderQueue queue)
    {
        Submit(queue.Target, queue.Position, queue.Shader);
    }

    public void SubmitShape<T>(T renderable, Vector2 position)
        where T : struct, IShapeRenderable
    {
        var box = ShapeRenderPool<T>.Rent();
        box.Value = renderable;
        _queue.Pooled.Add(box);
        Submit((IRenderable)box, position);
    }

    // Draws and empties the queue into the target that is set.
    private void DrawQueue()
    {
        if (_queue.Count == 0)
            return;

        var sortStart = Stopwatch.GetTimestamp();
        _queue.Sort();
        _frameStats.SortMs += Stopwatch.GetElapsedTime(sortStart).TotalMilliseconds;

        var flushStart = Stopwatch.GetTimestamp();
        DrawCommands(_queue);
        _queue.Clear();

        _frameStats.FlushMs += Stopwatch.GetElapsedTime(flushStart).TotalMilliseconds;
    }

    // Single draw loop for the sorted queue. target/viewport must already be set.
    // Interleaves two batchers (ours for sprites/text, ShapeBatch for debug shapes).
    private void DrawCommands(DrawCommandBuffer queue)
    {
        var shapeOpen = false;
        SamplerState? shapeSampler = null;
        DepthStencilState? shapeStencil = null;

        _batcher.Begin(ComputeTransform(), BlendState, (float)GameClient.Timing.TotalTime);

        for (var i = 0; i < queue.Count; i++)
        {
            ref var command = ref queue.Commands[queue.Order[i].Index];

            if (command.Kind == DrawCommandType.Shape)
            {
                if (!shapeOpen)
                {
                    // the sprites before it have to be on the target before the shapes go over them
                    _batcher.Flush();
                    if (i > 0)
                        _frameStats.BreaksByKind++;
                }
                else if (command.Sampler != shapeSampler)
                {
                    EndShapes();
                    _frameStats.BreaksBySampler++;
                    shapeOpen = false;
                }
                else if (!ReferenceEquals(GetMaskState(command.Mask, command.MaskId), shapeStencil))
                {
                    EndShapes();
                    _frameStats.BreaksByMask++;
                    shapeOpen = false;
                }

                if (!shapeOpen)
                {
                    shapeStencil = GetMaskState(command.Mask, command.MaskId);
                    BeginShapes(command.Sampler, shapeStencil, command.Mask == MaskMode.Write ? MaskWriteBlend : BlendState);
                    _frameStats.ShapeBatches++;
                    shapeOpen = true;
                    shapeSampler = command.Sampler;
                }

                command.Target!.Draw(this, command.Position);
                continue;
            }

            if (shapeOpen)
            {
                EndShapes();
                shapeOpen = false;
                _frameStats.BreaksByKind++;
            }

            _commandSampler = command.Sampler ?? DefaultSampler;

            var writesMask = command.Mask == MaskMode.Write;
            _batcher.SetStencil(GetMaskState(command.Mask, command.MaskId), writesMask ? MaskWriteBlend : null);
            _batcher.SetState(writesMask ? GetMaskShader(command.Shader) : command.Shader, _commandSampler);
            _batcher.Custom0 = command.Custom0;
            _batcher.Custom1 = command.Custom1;

            if (command.Kind == DrawCommandType.Sprite)
            {
                _batcher.Draw(command.Texture, command.Position, command.Source, command.Color, command.Rotation,
                    command.Origin, command.Scale, command.Effects);
            }
            else
            {
                command.Target!.Draw(this, command.Position);
            }
        }

        _batcher.Flush();

        if (shapeOpen)
            EndShapes();
    }

    #endregion

    public void BeginShapes(SamplerState? samplerState = null, DepthStencilState? depthStencilState = null)
        => BeginShapes(samplerState, depthStencilState, BlendState);

    private void BeginShapes(SamplerState? samplerState, DepthStencilState? depthStencilState, BlendState blendState)
    {
        var transform = ComputeTransform();

        _shapeBatch.Begin(
            view: transform,
            samplerState: samplerState ?? DefaultSampler,
            blendState: blendState,
            depthStencilState: depthStencilState);
    }

    public void EndShapes()
    {
        _shapeBatch.End();
    }
}
