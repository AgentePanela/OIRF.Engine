using System;
using System.Diagnostics;
using Engine.Client.Graphics.Lighting;
using Engine.Shared.Common;
using Engine.Shared.Extensions;
using Engine.Shared.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

// The field of view: what the eye of a viewport cannot see is covered by one color, over the world, like the FOV of
// Robust (SS14). The mask is drawn before the world (a target switch in the middle of a viewport would throw away
// what was drawn in it) and put over it after the queue.
public sealed partial class RenderManager
{
    // the biggest mask, the HiDef limit
    private const int MaxFovSize = 4096;

    private bool _fov;

    // premultiplied
    private Vector4 _fovColor;

    private void InitFov()
    {
        _cfg.Subs(FovCvars.Enabled, v => _fov = v);
        _cfg.Subs(FovCvars.Color, v => _fovColor = ParseFovColor(v));
    }

    private static Vector4 ParseFovColor(string hex)
    {
        Color color;
        try
        {
            color = hex.FromHex();
        }
        catch (FormatException)
        {
            Log.Warn($"fov.color '{hex}' is not a #RRGGBBAA color, using black");
            color = Color.Black;
        }

        var value = color.ToVector4();
        return new Vector4(value.X * value.W, value.Y * value.W, value.Z * value.W, value.W);
    }

    /// <summary>
    /// Draws the mask of what the eye of <paramref name="viewport"/> does not see, before its world. False when it has
    /// no field of view.
    /// </summary>
    private bool BuildFovMask(RenderViewport viewport)
    {
        viewport.HadFov = false;

        if (!_fov || !viewport.FovEnabled || viewport.Eye is not { } eye || LightRenderer is not { } lights
            || GameClient.GameState != GameState.Running || _shaders.FovInstance is null)
            return false;

        var start = Stopwatch.GetTimestamp();
        var device = GameClient.GraphicsDevice;
        var view = viewport.Camera.WorldBounds;

        // a pixel of the mask for every pixel of the screen, so the edge is as sharp as the screen
        var scale = viewport.RenderScale * viewport.Camera.Zoom;
        var margin = 2f / scale;
        var left = view.Left - margin;
        var top = view.Top - margin;
        var right = view.Right + margin;
        var bottom = view.Bottom + margin;

        scale = MathF.Min(scale, (MaxFovSize - 1) / MathF.Max(right - left, bottom - top));
        left = MathF.Floor(left * scale) / scale;
        top = MathF.Floor(top * scale) / scale;

        var size = new Point(
            Math.Clamp((int)MathF.Ceiling((right - left) * scale), 1, MaxFovSize),
            Math.Clamp((int)MathF.Ceiling((bottom - top) * scale), 1, MaxFovSize));
        var bounds = new RectangleF(left, top, size.X / scale, size.Y / scale);

        var target = viewport.EnsureFovTarget(device, size);

        // read by what was drawn before (the last viewport's mask, the atlases)
        device.Textures[0] = null;
        device.Textures[1] = null;
        device.Textures[3] = null;
        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);

        if (!lights.DrawFovMask(eye, viewport.EyeOffset, bounds, scale, new Point(target.Width, target.Height),
                _frameStats.Light, out var eyePosition))
            return false;

        viewport.FovMaskBounds = bounds;
        viewport.FovMaskSize = size;
        viewport.FovMaskTransform = new Vector4(
            scale / target.Width, scale / target.Height,
            -bounds.X * scale / target.Width, -bounds.Y * scale / target.Height);
        viewport.FovEyePosition = eyePosition;
        viewport.HadFov = true;

        _frameStats.Light.FovViewports++;
        _frameStats.Light.FovMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return true;
    }

    /// <summary>
    /// Covers what the eye of <paramref name="viewport"/> does not see, over the world just drawn.
    /// </summary>
    private void DrawFov(RenderViewport viewport)
    {
        if (!viewport.HadFov || _shaders.FovInstance is not { Effect: { } effect } shader)
            return;

        var start = Stopwatch.GetTimestamp();
        var device = GameClient.GraphicsDevice;

        effect.Parameters["FovMask"]?.SetValue(viewport.FovTarget);
        effect.Parameters["FovMaskTransform"]?.SetValue(viewport.FovMaskTransform);
        effect.Parameters["FovColor"]?.SetValue(_fovColor);
        effect.Parameters["FovEye"]?.SetValue(new Vector4(viewport.FovEyePosition, viewport.FovRadius,
            viewport.FovRadiusSoftness));
        device.SamplerStates[3] = SamplerState.PointClamp;

        // one quad over the view, each pixel reads the mask where it is
        var view = viewport.Camera.WorldBounds;
        _batcher.Begin(ComputeTransform(), BlendState.AlphaBlend, (float)GameClient.Timing.TotalTime);
        _batcher.SetState(shader, SamplerState.PointClamp);
        _batcher.Lighting = new TextureLighting(LightBaseMode.Pixel, Vector2.Zero, 1f);
        _batcher.Draw(_batcher.White, new Vector2(view.X, view.Y), new Rectangle(0, 0, 1, 1), Color.White, 0f,
            Vector2.Zero, new Vector2(view.Width, view.Height), SpriteEffects.None);
        _batcher.Flush();
        _batcher.Lighting = default;

        _frameStats.Light.FovMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}
