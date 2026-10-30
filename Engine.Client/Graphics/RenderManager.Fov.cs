using System;
using System.Diagnostics;
using Engine.Client.Graphics.Lighting;
using Engine.Shared.Common;
using Engine.Shared.Extensions;
using Engine.Shared.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;
public sealed partial class RenderManager
{
    private int _maxFovSize => _lightMaxSize;
    private Vector4 _fovColor;

    private void InitFov()
    {
        _cfg.Subs(LightingCvars.FovColor, v => _fovColor = ParseFovColor(v));
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

    private bool BuildFovMask(RenderViewport viewport)
    {
        viewport.HadFov = false;

        if (!viewport.FovEnabled || viewport.Eye is not { } eye || LightRenderer is not { } lights
            || GameClient.GameState != GameState.Running || _shaders.FovInstance is null)
            return false;

        var start = Stopwatch.GetTimestamp();
        var device = GameClient.GraphicsDevice;
        var view = viewport.Camera.WorldBounds;

        // a pixel of the mask for every pixel of the screen
        var scale = viewport.RenderScale * viewport.Camera.Zoom;
        var margin = 2f / scale;
        var left = view.Left - margin;
        var top = view.Top - margin;
        var right = view.Right + margin;
        var bottom = view.Bottom + margin;

        scale = MathF.Min(scale, (_maxFovSize - 1) / MathF.Max(right - left, bottom - top));
        left = MathF.Floor(left * scale) / scale;
        top = MathF.Floor(top * scale) / scale;

        var size = new Point(
            Math.Clamp((int)MathF.Ceiling((right - left) * scale), 1, _maxFovSize),
            Math.Clamp((int)MathF.Ceiling((bottom - top) * scale), 1, _maxFovSize));
        var bounds = new RectangleF(left, top, size.X / scale, size.Y / scale);

        var target = viewport.EnsureFovTarget(device, size);

        // read by what was drawn before
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

        _frameStats.Light.ViewportsWithFov++;
        _frameStats.Light.FovMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return true;
    }

    // Covers what the eye of the viewport does not see, over the world just drawn.
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
