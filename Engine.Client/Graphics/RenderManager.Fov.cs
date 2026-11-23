using System;
using System.Diagnostics;
using Engine.Client.Graphics.Lighting;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.Common;
using Engine.Shared.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;
public sealed partial class RenderManager
{
    private int _maxFovSize => _lightMaxSize;
    private Vector4 _fovColor;

    // the mask being built
    private bool _buildingFov;
    private RectangleF _fovBounds;
    private float _fovScale;
    private Point _fovTargetSize;

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
            || GameClient.GameState != GameState.Running || !lights.TryGetEyePosition(eye, out var eyePosition))
            return false;

        var start = Stopwatch.GetTimestamp();
        var device = GameClient.GraphicsDevice;
        var view = viewport.Camera.WorldBounds;
        eyePosition += viewport.EyeOffset;

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
        device.Textures[2] = null;
        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);

        _buildingFov = true;
        _fovBounds = bounds;
        _fovScale = scale;
        _fovTargetSize = new Point(target.Width, target.Height);

        // what the renderer submits goes into the mask
        (_queue, _overlayQueue) = (_overlayQueue, _queue);
        var blend = BlendState;
        BlendState = BlendState.Opaque;
        _transformOverride = Matrix.CreateTranslation(-bounds.X, -bounds.Y, 0f) * Matrix.CreateScale(scale, scale, 1f);

        try
        {
            viewport.Fov.BuildMask(new FovMaskContext(this, viewport, eye, eyePosition, bounds));
            DrawQueue();
        }
        finally
        {
            _transformOverride = null;
            BlendState = blend;
            (_queue, _overlayQueue) = (_overlayQueue, _queue);
            _buildingFov = false;
        }

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

    // FovMaskContext.DrawShadows
    internal void DrawFovShadows(Vector2 from, float radius)
    {
        if (!_buildingFov || LightRenderer is not { } lights)
            return;

        lights.DrawFovShadows(from, radius, _fovBounds, _fovScale, _fovTargetSize, _frameStats.Light);
    }

    private void UseFovMask(RenderViewport? viewport)
    {
        if (viewport is not { HadFov: true } || viewport.FovTarget is not { } target)
        {
            _batcher.FovMask = null;
            return;
        }

        _batcher.FovMask = target;
        _batcher.FovMaskTransform = viewport.FovMaskTransform;
        _batcher.FovColor = _fovColor;
        _batcher.FovEye = new Vector4(viewport.FovEyePosition, viewport.FovRadius, viewport.FovRadiusSoftness);
    }

    // what is out of sight, over the world just drawn and lit
    private void ComposeFov(RenderViewport viewport)
    {
        if (!viewport.HadFov)
            return;

        var start = Stopwatch.GetTimestamp();
        viewport.Fov.Compose(new FovComposeContext(this, viewport, _fovColor, _shaders.FovInstance));
        _frameStats.Light.FovMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    internal void DrawFovQuad(RenderViewport viewport, ShaderInstance shader, bool filter)
    {
        if (shader.IsDisposed)
            return;

        var device = GameClient.GraphicsDevice;
        Texture2D texture = _batcher.White;
        var blend = BlendState.AlphaBlend;

        if (filter)
        {
            texture = viewport.SwapTargets(device);
            device.SetRenderTarget(viewport.Target);
            blend = BlendState.Opaque;
        }

        // the corners of the target taken to the world
        var view = viewport.GetViewMatrix();
        var inverse = Matrix.Invert(view);
        var size = viewport.TargetSize;

        _batcher.Begin(view, blend, (float)GameClient.Timing.TotalTime);
        _batcher.SetState(shader, SamplerState.PointClamp);
        _batcher.Custom0 = shader.DefaultCustom0;
        _batcher.Custom1 = shader.DefaultCustom1;
        _batcher.Lighting = TextureLighting.Unshaded;
        _batcher.DrawQuad(texture,
            Corner(0f, 0f), Corner(size.X, 0f), Corner(0f, size.Y), Corner(size.X, size.Y));
        _batcher.Flush();
        _batcher.Lighting = default;
        _batcher.Custom0 = _batcher.Custom1 = default;

        VertexPositionColorTexture Corner(float x, float y)
            => new(new Vector3(Vector2.Transform(new Vector2(x, y), inverse), 0f), Color.White,
                new Vector2(x / size.X, y / size.Y));
    }
}
