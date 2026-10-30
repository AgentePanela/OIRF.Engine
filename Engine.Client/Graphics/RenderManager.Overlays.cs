using System.Collections.Generic;
using System.Diagnostics;
using Engine.Client.Graphics.Shaders;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    private readonly List<RenderOverlay> _overlays = new();

    /// <summary>
    /// What the overlays of a stage submit, so it is not mixed with what is waiting for the world.
    /// </summary>
    private DrawCommandBuffer _overlayQueue = new();

    private bool _screenSpace;

    public IReadOnlyList<RenderOverlay> Overlays => _overlays;

    public void AddOverlay(RenderOverlay overlay)
    {
        if (_overlays.Contains(overlay))
            return;

        // after the ones with the same order, so those keep the order they were added in
        var index = _overlays.Count;
        while (index > 0 && _overlays[index - 1].Order > overlay.Order)
            index--;

        _overlays.Insert(index, overlay);
    }

    public void RemoveOverlay(RenderOverlay overlay)
        => _overlays.Remove(overlay);

    private void DrawOverlays(RenderViewport viewport, RenderStage stage, float dt)
    {
        // the world stage goes into the queue of the world itself, to be sorted with it
        var ownQueue = stage != RenderStage.World;
        var any = false;

        foreach (var overlay in _overlays)
        {
            if (!overlay.Enabled || overlay.Stage != stage || !overlay.AppliesTo(viewport))
                continue;

            if (!any)
            {
                any = true;
                if (ownQueue)
                    (_queue, _overlayQueue) = (_overlayQueue, _queue);
            }

            RawTexture? screen = null;
            if (ownQueue && overlay.RequestScreenTexture && stage != RenderStage.LightMap)
            {
                // what the overlays before it submitted has to be in the frame it gets
                DrawQueue();
                screen = TakeScreenTexture(viewport);
            }

            var start = Stopwatch.GetTimestamp();
            overlay.Draw(new RenderOverlayArgs(this, viewport, stage, dt, screen));
            _frameStats.Overlays.Add(new RenderStats.OverlayStat(overlay.GetType().Name, stage,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds));

            // and it has to be drawn before the next one takes the frame, or swaps it away
            if (screen is not null)
                DrawQueue();
        }

        if (any && ownQueue)
        {
            DrawQueue();
            (_queue, _overlayQueue) = (_overlayQueue, _queue);
        }
    }

    private RawTexture TakeScreenTexture(RenderViewport viewport)
    {
        var device = GameClient.GraphicsDevice;
        var frame = viewport.SwapTargets(device);

        device.SetRenderTarget(viewport.Target);
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
        _spriteBatch.Draw(frame, Vector2.Zero, Color.White);
        _spriteBatch.End();

        return new RawTexture(frame) { Origin = Vector2.Zero };
    }

    // Each post effect draws the whole frame again through its shader, into the viewport's other target.
    private void ApplyPostEffects(RenderViewport viewport)
    {
        if (viewport.PostEffects.Count == 0)
            return;

        var device = GameClient.GraphicsDevice;
        var start = Stopwatch.GetTimestamp();

        foreach (var shader in viewport.PostEffects)
        {
            if (shader.IsDisposed)
                continue;

            var frame = viewport.SwapTargets(device);
            device.SetRenderTarget(viewport.Target);
            device.Clear(Color.Black);

            // the frame replaces what is there, unless the shader asks for another blend
            _batcher.Begin(Matrix.Identity, BlendState.Opaque, (float)GameClient.Timing.TotalTime);
            _batcher.SetState(shader, SamplerState.PointClamp);
            _batcher.Custom0 = shader.DefaultCustom0;
            _batcher.Custom1 = shader.DefaultCustom1;
            _batcher.Draw(frame, frame.Bounds, frame.Bounds, Color.White);
            _batcher.Flush();

            _frameStats.PostEffects++;
        }

        _frameStats.PostProcessMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}
