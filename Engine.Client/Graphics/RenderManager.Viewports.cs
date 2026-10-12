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

        if (PresentMainViewport && MainViewport.ColorTarget is { } frame)
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
        _frameStats.PresentMs = Stopwatch.GetElapsedTime(presentStart).TotalMilliseconds;
        _frameStats.TotalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        Stats.CopyFrom(_frameStats);
        _frameStats.Reset();
    }

    private void DrawViewport(RenderViewport viewport, float dt, Color clearColor)
    {
        var device = GameClient.GraphicsDevice;
        var start = Stopwatch.GetTimestamp();

        device.SetRenderTarget(viewport.EnsureTarget(device));
        device.Clear(viewport.ClearColor ?? clearColor);

        _current = viewport;
        viewport.Camera.CacheFrame();

        if (GameClient.GameState == GameState.Running)
        {
            var submitStart = Stopwatch.GetTimestamp();
            GameClient.EntityManager.DrawSystems(new DrawContext(dt, viewport, viewport.Camera.WorldBounds));
            _frameStats.SubmitMs += Stopwatch.GetElapsedTime(submitStart).TotalMilliseconds;
        }

        var items = _queue.Entries.Count;
        DrawQueue();
        _current = null;

        _frameStats.Viewports.Add(new RenderStats.ViewportStat(viewport.Name, viewport.TargetSize.X, viewport.TargetSize.Y,
            items, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
    }

    // The transform every batch of the viewport being drawn starts with.
    private Matrix ComputeTransform()
        => _current?.GetViewMatrix() ?? Matrix.Identity;

    #endregion
}