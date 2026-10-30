using System;
using System.Diagnostics;
using Engine.Client.Graphics.Fonts;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    #region Viewports

    public RenderViewport CreateViewport(string name, Vector2 size, float renderScale = 1f)
    {
        var viewport = new RenderViewport(name, new Camera2D());
        viewport.Resize(size, renderScale);
        _viewports.Add(viewport);
        return viewport;
    }

    public void RemoveViewport(RenderViewport viewport)
    {
        if (_viewports.Remove(viewport))
            viewport.Dispose();
    }

    public void ReportCulled(int count = 1)
        => _frameStats.Culled += count;

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        return _camera.ScreenToWorld(screenPos);
    }

    /// <summary>
    /// Fits the main viewport to the window, following <see cref="MainViewportScaling"/>.
    /// </summary>
    internal void SyncMainViewport()
    {
        if (MainViewportScaling == MainViewportScaling.Manual || GameClient.GraphicsDevice is null)
            return;

        var pp = GameClient.GraphicsDevice.PresentationParameters;
        if (pp.BackBufferWidth <= 0 || pp.BackBufferHeight <= 0)
            return;

        if (MainViewportScaling == MainViewportScaling.Native)
        {
            MainViewport.Resize(new Vector2(pp.BackBufferWidth, pp.BackBufferHeight));
            MainViewport.ScreenOffset = Vector2.Zero;
            return;
        }

        _viewport.UpdateScaleMatrix();

        // scaling outer makes the game bigger than the window: only what fits is drawn
        var target = new Point(
            Math.Min(_viewport.VirtualWidth, pp.BackBufferWidth),
            Math.Min(_viewport.VirtualHeight, pp.BackBufferHeight));

        MainViewport.Resize(new Vector2(GameClient.Options.Width, GameClient.Options.Height), _viewport.Scale, target);
        MainViewport.ScreenOffset = new Vector2(
            (pp.BackBufferWidth - target.X) / 2,
            (pp.BackBufferHeight - target.Y) / 2);
    }

    /// <summary>
    /// Draws every enabled viewport and puts the main one on the window.
    /// </summary>
    internal void RenderFrame(float dt, Color clearColor)
    {
        var device = GameClient.GraphicsDevice;
        var start = Stopwatch.GetTimestamp();
        var before = device.Metrics;

        SyncMainViewport();

        // null until the shaders are loaded, the plain sprite effect is used until then
        _batcher.DefaultShader = _shaders.DefaultInstance;

        // textures that have to be ready before any viewport draws with them
        BeforeFrame?.Invoke();

        // the lights every viewport needs, drawn once for all of them
        PrepareLighting();

        // What is in the queue by now was submitted outside of a viewport draw (a scene's Draw), it belongs to
        // the main one.
        (_queue, _mainQueue) = (_mainQueue, _queue);
        foreach (var viewport in _viewports)
        {
            if (viewport.Enabled)
                DrawViewport(viewport, dt, clearColor);
        }
        (_queue, _mainQueue) = (_mainQueue, _queue);

        DrawViewport(MainViewport, dt, clearColor);

        var presentStart = Stopwatch.GetTimestamp();
        device.SetRenderTarget(null);
        device.Clear(clearColor);

        if (PresentMainViewport && MainViewport.Target is { } frame)
        {
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
            _spriteBatch.Draw(frame, MainViewport.ScreenOffset, Color.White);
            _spriteBatch.End();
        }

        var after = device.Metrics;
        _frameStats.DrawCalls = (int)(after.DrawCount - before.DrawCount);
        _frameStats.Primitives = (int)(after.PrimitiveCount - before.PrimitiveCount);
        _frameStats.TextureSwaps = (int)(after.TextureCount - before.TextureCount);
        _frameStats.TargetSwitches = (int)(after.TargetCount - before.TargetCount);
        _frameStats.SharedShaders = _shaders.SharedInstances;
        _frameStats.OwnedShaders = _shaders.OwnedInstances;
        _frameStats.PresentMs = Stopwatch.GetElapsedTime(presentStart).TotalMilliseconds;
        _frameStats.TargetBytes += LightRenderer?.Bytes ?? 0;
        _frameStats.TotalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        Stats.CopyFrom(_frameStats);
        _frameStats.Reset();
    }

    private void DrawViewport(RenderViewport viewport, float dt, Color clearColor)
    {
        var device = GameClient.GraphicsDevice;
        var start = Stopwatch.GetTimestamp();

        _current = viewport;
        viewport.Camera.CacheFrame();

        var lit = BuildLightMap(viewport, dt);
        BuildFovMask(viewport);
        device.SetRenderTarget(viewport.EnsureTarget(device));
        device.Clear(viewport.ClearColor ?? clearColor);

        // the world reads the light map while it is drawn
        UseLightMap(lit ? viewport : null);

        DrawOverlays(viewport, RenderStage.BelowWorld, dt);

        if (GameClient.GameState == GameState.Running)
        {
            var submitStart = Stopwatch.GetTimestamp();
            GameClient.EntityManager.DrawSystems(new DrawContext(dt, viewport, viewport.Camera.WorldBounds));
            _frameStats.SubmitMs += Stopwatch.GetElapsedTime(submitStart).TotalMilliseconds;
        }

        DrawOverlays(viewport, RenderStage.World, dt);

        var items = _queue.Count;
        _collectLightBases = lit && _lightDebugBase;
        DrawQueue();
        _collectLightBases = false;

        UseLightMap(null);

        // over the world and light
        DrawFov(viewport);

        ApplyBloom(viewport);

        DrawLightBases();

        DrawOverlays(viewport, RenderStage.AfterLighting, dt);

        _screenSpace = true;
        DrawOverlays(viewport, RenderStage.Screen, dt);
        _screenSpace = false;

        ApplyPostEffects(viewport);

        _current = null;
        _frameStats.TargetBytes += viewport.TargetBytes;

        _frameStats.Viewports.Add(new RenderStats.ViewportStat(viewport.Name, viewport.TargetSize.X, viewport.TargetSize.Y,
            items, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
    }

    // The transform every batch of the viewport being drawn starts with.
    private Matrix ComputeTransform()
        => _transformOverride ?? (_screenSpace ? Matrix.Identity : _current?.GetViewMatrix() ?? Matrix.Identity);

    /// <summary>
    /// Raised at the start of every frame, before any viewport is drawn: the only point where it is safe to draw
    /// into a texture of your own with <see cref="RenderToTarget"/>.
    /// </summary>
    internal event Action? BeforeFrame;

    // set while RenderToTarget draws, in place of the transform of a viewport
    private Matrix? _transformOverride;

    /// <summary>
    /// Clears <paramref name="target"/> to transparent and draws into it what <paramref name="submit"/> submits,
    /// through <paramref name="transform"/>. Only from <see cref="BeforeFrame"/>: switching targets in the middle of
    /// a viewport would throw away what was drawn in it so far.
    /// </summary>
    internal void RenderToTarget<TState>(RenderTarget2D target, Matrix transform, TState state, Action<TState> submit)
    {
        var device = GameClient.GraphicsDevice;

        // its own queue, so what a scene already submitted for the world is not drawn in here
        (_queue, _overlayQueue) = (_overlayQueue, _queue);

        submit(state);

        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);

        _transformOverride = transform;
        DrawQueue();
        _transformOverride = null;

        (_queue, _overlayQueue) = (_overlayQueue, _queue);
    }

    #endregion
}