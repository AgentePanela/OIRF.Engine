using System;
using System.Diagnostics;
using Engine.Client.Graphics.Lighting;
using Engine.Shared.Common;
using Engine.Shared.Configuration;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// What fills the light map of a viewport
/// </summary>
internal interface ILightRenderer
{
    Color GetAmbientColor();

    void DrawLights(RenderViewport viewport, RectangleF bounds);
}

public sealed partial class RenderManager
{
    [Dependency] private readonly LightingManager _lighting = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    internal ILightRenderer? LightRenderer { get; set; }

    private float _lightResolution;
    private float _lightMargin;
    private int _lightMaxSize;
    private float _lightPixelSize;
    private float _lightDither;
    private float _overbright = 1f;

    private void InitLighting()
    {
        _cfg.Subs(LightingCvars.Resolution, v => _lightResolution = MathF.Max(v, 0.01f));
        _cfg.Subs(LightingCvars.Margin, v => _lightMargin = MathF.Max(v, 0f));
        _cfg.Subs(LightingCvars.MaxSize, v => _lightMaxSize = Math.Max(v, 16));
        _cfg.Subs(LightingCvars.Overbright, v => _overbright = MathF.Max(v, 1f));
        _cfg.Subs(LightingCvars.Dither, v => _lightDither = v ? 1f : 0f);

        // 0 is what turns the snap off in the shader
        _cfg.Subs(LightingCvars.Pixelated, _ => UpdatePixelSize());
        _cfg.Subs(LightingCvars.PixelSize, _ => UpdatePixelSize());
    }

    private void UpdatePixelSize()
        => _lightPixelSize = _cfg.Get(LightingCvars.Pixelated) ? MathF.Max(_cfg.Get(LightingCvars.PixelSize), 0.01f) : 0f;

    /// <summary>
    /// Builds the light map of <paramref name="viewport"/> for this frame, before its world is drawn. False when the
    /// viewport is drawn unlit.
    /// </summary>
    private bool BuildLightMap(RenderViewport viewport, float dt)
    {
        viewport.WasLit = false;

        if (!_lighting.Enabled || !viewport.LightingEnabled || LightRenderer is not { } lights
            || GameClient.GameState != GameState.Running || _shaders.DefaultInstance is null)
            return false;

        var start = Stopwatch.GetTimestamp();
        var device = GameClient.GraphicsDevice;

        var view = viewport.Camera.WorldBounds;
        var left = view.Left - _lightMargin;
        var top = view.Top - _lightMargin;
        var right = view.Right + _lightMargin;
        var bottom = view.Bottom + _lightMargin;

        // fewer texels per world unit when the view is too big for the largest light map
        var maxSize = _lightMaxSize;
        var scale = MathF.Min(_lightResolution, (maxSize - 1) / MathF.Max(right - left, bottom - top));

        left = MathF.Floor(left * scale) / scale;
        top = MathF.Floor(top * scale) / scale;

        var size = new Point(
            Math.Clamp((int)MathF.Ceiling((right - left) * scale), 1, maxSize),
            Math.Clamp((int)MathF.Ceiling((bottom - top) * scale), 1, maxSize));
        var bounds = new RectangleF(left, top, size.X / scale, size.Y / scale);

        var target = viewport.EnsureLightTarget(device, size);

        // the last viewport drawn read it, it cannot be drawn into while it is still bound
        device.Textures[1] = null;
        device.SetRenderTarget(target);

        var ambient = lights.GetAmbientColor().ToVector3() / _overbright;
        device.Clear(new Color(ambient.X, ambient.Y, ambient.Z, 1f));

        var lightView = Matrix.CreateTranslation(-left, -top, 0f) * Matrix.CreateScale(scale, scale, 1f);

        _batcher.LightMap = null;
        _batcher.Begin(lightView, BlendState.Additive, (float)GameClient.Timing.TotalTime);
        lights.DrawLights(viewport, bounds);
        _batcher.Flush();

        // what the content adds to the light, in world space and adding up like the lights
        var blend = BlendState;
        BlendState = BlendState.Additive;
        _transformOverride = lightView;
        DrawOverlays(viewport, RenderStage.LightMap, dt);
        _transformOverride = null;
        BlendState = blend;

        viewport.LightMapBounds = bounds;
        viewport.LightMapSize = size;
        viewport.LightMapTransform = new Vector4(
            scale / target.Width, scale / target.Height,
            -left * scale / target.Width, -top * scale / target.Height);
        viewport.WasLit = true;

        _frameStats.LightMapTexels += size.X * size.Y;
        _frameStats.LightMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return true;
    }

    /// <summary>
    /// What everything drawn from now on reads the light from: the light map of <paramref name="viewport"/>, or no
    /// light at all.
    /// </summary>
    private void UseLightMap(RenderViewport? viewport)
    {
        if (viewport is not { WasLit: true, LightTarget: { } target })
        {
            _batcher.LightMap = null;
            return;
        }

        _batcher.LightMap = target;
        _batcher.LightMapTransform = viewport.LightMapTransform;
        _batcher.LightParams = new Vector4(0f, _lightPixelSize, _overbright, _lightDither);
    }

    /// <summary>
    /// Draws one light into the light map being built. Only from <see cref="ILightRenderer.DrawLights"/>.
    /// </summary>
    /// <param name="mask">Its alpha is the shape of the light, null for a round one.</param>
    /// <param name="halfCone">Half the cone angle in radians, pi or more for no cone.</param>
    /// <param name="coneSoftness">How much of the cone fades at its edges, 0-1.</param>
    /// <param name="curve">Between linear (0) and quadratic (1) fading.</param>
    internal void DrawLight(Texture2D? mask, Rectangle maskRegion, Vector2 center, float radius, float rotation,
        Color color, float brightness, float halfCone, float coneSoftness, float falloff, float curve)
    {
        if (_shaders.LightInstance is not { } shader)
            return;

        var texture = mask ?? _batcher.White;
        var source = mask is null ? new Rectangle(0, 0, 1, 1) : maskRegion;
        var size = new Vector2(source.Width, source.Height);

        _batcher.SetState(shader, SamplerState.LinearClamp);
        _batcher.Custom0 = new Vector4(brightness / _overbright, halfCone, coneSoftness, 0f);
        _batcher.Custom1 = new Vector4(falloff, curve, 0f, 0f);
        _batcher.Lighting = default;
        _batcher.Draw(texture, center, source, color, rotation, size / 2f, new Vector2(radius * 2f) / size,
            SpriteEffects.None);

        _frameStats.LightsDrawn++;
    }
}
