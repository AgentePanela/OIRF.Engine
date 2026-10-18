using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Engine.Client.Graphics.Lighting;
using Engine.Client.Graphics.Shaders;
using Engine.Shared;
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

    void CollectLights(RenderViewport viewport, RectangleF bounds, float softnessScale, List<LightDraw> lights);

    void CollectShadowSegments(in LightDraw light, List<ShadowSegment> segments);
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
    private float _lightSoftnessScale;
    private float _overbright = 1f;

    private readonly List<LightDraw> _lights = new();
    private readonly List<ShadowSegment> _segments = new();
    private readonly LightAtlas _atlas = new();
    private readonly ShadowBatch _shadowBatch = new();
    private bool _warnedAtlasFull;

    // a light in its square
    private static readonly BlendState LightSlotBlend = new()
    {
        ColorSourceBlend = Blend.InverseDestinationAlpha,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
    };

    // the squares added to the light map
    private static readonly BlendState AddColorBlend = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
    };

    private void InitLighting()
    {
        _cfg.Subs(LightingCvars.SoftnessScale, v => _lightSoftnessScale = MathF.Max(v, 0f));
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

        _batcher.LightMap = null;

        // each light with its shadows in its own square, before the light map is set
        _lights.Clear();
        lights.CollectLights(viewport, bounds, _lightSoftnessScale, _lights);
        DrawLightSlots(lights, scale);

        var target = viewport.EnsureLightTarget(device, size);

        // read by what was drawn before (the squares, the last viewport)
        device.Textures[0] = null;
        device.Textures[1] = null;
        device.SetRenderTarget(target);

        var ambient = lights.GetAmbientColor().ToVector3() / _overbright;
        device.Clear(new Color(ambient.X, ambient.Y, ambient.Z, 1f));

        var lightView = Matrix.CreateTranslation(-left, -top, 0f) * Matrix.CreateScale(scale, scale, 1f);

        _batcher.Begin(lightView, BlendState.Additive, (float)GameClient.Timing.TotalTime);
        _batcher.SetStencil(DepthStencilState.None, AddColorBlend);
        foreach (ref readonly var light in CollectionsMarshal.AsSpan(_lights))
        {
            if (light.Page < 0)
                continue;

            _batcher.SetState(null, SamplerState.LinearClamp);
            _batcher.Draw(_atlas.GetPage(device, light.Page), light.Center, light.Region, Color.White, 0f,
                new Vector2(light.Region.Width / 2f, light.Region.Height / 2f), new Vector2(1f / light.SlotScale),
                SpriteEffects.None);
        }

        _batcher.Flush();
        _batcher.SetStencil(DepthStencilState.None, null);

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
    /// Gives every light of <see cref="_lights"/> a square in the atlas, at <paramref name="scale"/> texels per world
    /// unit, and draws it there with its shadows
    /// </summary>
    private void DrawLightSlots(ILightRenderer lights, float scale)
    {
        var device = GameClient.GraphicsDevice;
        var span = CollectionsMarshal.AsSpan(_lights);

        for (var i = 0; i < span.Length; i++)
        {
            var size = LightAtlas.RegionSize(span[i].Radius, scale, out span[i].SlotScale);
            span[i].Region = new Rectangle(0, 0, size, size);
        }

        // the biggest first, they pack better
        _lights.Sort(static (a, b) => b.Region.Width.CompareTo(a.Region.Width));
        span = CollectionsMarshal.AsSpan(_lights);

        _atlas.Reset();
        for (var i = 0; i < span.Length; i++)
        {
            if (_atlas.TryPlace(span[i].Region.Width, out span[i].Page, out var at))
            {
                span[i].Region.Location = at;
                continue;
            }

            if (!_warnedAtlasFull)
            {
                Log.Warn($"The light atlas is full ({LightAtlas.MaxPages} pages): lights past it are not drawn");
                _warnedAtlasFull = true;
            }
        }

        if (_shaders.LightInstance is not { } lightShader)
            return;

        var shadowEffect = _shaders.GetShader("LightShadow");
        var time = (float)GameClient.Timing.TotalTime;

        for (var page = 0; page < _atlas.PagesUsed; page++)
        {
            // a page can still be bound from adding its squares to the last light map
            device.Textures[0] = null;
            device.SetRenderTarget(_atlas.GetPage(device, page));
            device.Clear(Color.Transparent);

            if (shadowEffect is not null)
            {
                foreach (ref readonly var light in span)
                {
                    if (light.Page != page || !light.CastShadows)
                        continue;

                    _segments.Clear();
                    lights.CollectShadowSegments(light, _segments);
                    foreach (var segment in _segments)
                        _shadowBatch.Add(device, shadowEffect, segment, light);
                }

                _shadowBatch.Flush(device, shadowEffect);
            }

            // in page pixels
            _batcher.Begin(Matrix.Identity, BlendState.Additive, time);
            _batcher.SetStencil(DepthStencilState.None, LightSlotBlend);
            foreach (ref readonly var light in span)
            {
                if (light.Page == page)
                    DrawLightInSlot(lightShader, light);
            }

            _batcher.Flush();
            _batcher.SetStencil(DepthStencilState.None, null);
        }

        _frameStats.ShadowSegments += _shadowBatch.Count;
        _shadowBatch.Count = 0;
        _frameStats.LightAtlasPages = Math.Max(_frameStats.LightAtlasPages, _atlas.PagesUsed);
    }

    // one light, filling its square: 2 radius wide at the square scale, turned with it
    private void DrawLightInSlot(ShaderInstance shader, in LightDraw light)
    {
        var texture = light.Mask ?? _batcher.White;
        var source = light.Mask is null ? new Rectangle(0, 0, 1, 1) : light.MaskRegion;
        var size = new Vector2(source.Width, source.Height);

        _batcher.SetState(shader, SamplerState.LinearClamp);
        _batcher.Custom0 = new Vector4(light.Brightness / _overbright, light.HalfCone, light.ConeSoftness, 0f);
        _batcher.Custom1 = new Vector4(light.Falloff, light.Curve, 0f, 0f);
        _batcher.Lighting = default;
        _batcher.Draw(texture, light.Region.Center.ToVector2(), source, light.Color, light.Rotation, size / 2f,
            new Vector2(light.Radius * 2f * light.SlotScale) / size, SpriteEffects.None);

        _frameStats.LightsDrawn++;
    }
}
