using System;
using System.Collections.Generic;
using System.Diagnostics;
using Engine.Client.Graphics.Batching;
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

    void CollectLights(RectangleF bounds, float softnessScale, List<LightDraw> lights);

    void CollectShadowSegments(in LightDraw light, List<ShadowSegment> segments);

    /// <summary>
    /// The edges of the see-through occluders that is tinted
    /// </summary>
    void CollectSeeThroughSegments(in LightDraw light, List<ShadowSegment> segments);

    /// <summary>
    /// Moves to <paramref name="areas"/> where the occluders changed since the last call.
    /// </summary>
    void TakeOccluderChanges(List<RectangleF> areas);

    /// <summary>
    /// Video memory of what it keeps between frames.
    /// </summary>
    long Bytes { get; }

    /// <summary>
    /// Draws, before any viewport, what the light maps of <paramref name="layouts"/> are made of.
    /// </summary>
    void Prepare(List<LightMapLayout> layouts, LightSettings settings, double now, LightStats stats);

    /// <summary>
    /// Adds the light under <paramref name="layout"/> to the light map that is set, through
    /// <paramref name="lightView"/>.
    /// </summary>
    void Composite(LightMapLayout layout, Matrix lightView);

    /// <summary>
    /// Draws into the alpha of the target that is set what the occluders hide from
    /// <paramref name="eye"/> - Return False when the eye is nowhere to see from.
    /// </summary>
    bool DrawFovMask(EntityUid eye, Vector2 offset, RectangleF bounds, float scale, Point targetSize, LightStats stats,
        out Vector2 eyePosition);
}

public sealed partial class RenderManager
{
    [Dependency] private readonly LightingManager _lighting = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    internal ILightRenderer? LightRenderer { get; set; }

    /// <summary>
    /// The batcher everything is drawn with, for the light renderer.
    /// </summary>
    internal SpriteBatcher Batcher => _batcher;

    private float _lightResolution;
    private float _lightMargin;
    private int _lightMaxSize;
    private float _lightPixelSize;
    private float _lightDither;
    private float _lightSoftnessScale;
    private bool _lightCache;
    private float _lightStaticDelay;
    private float _overbright = 1f;

    // what the light map of every lit viewport covers this frame
    private readonly Dictionary<RenderViewport, LightMapLayout> _lightLayouts = new();
    private readonly List<LightMapLayout> _layoutList = new();

    private void InitLighting()
    {
        _cfg.Subs(LightingCvars.SoftnessScale, v => _lightSoftnessScale = MathF.Max(v, 0f));
        _cfg.Subs(LightingCvars.Resolution, v => _lightResolution = MathF.Max(v, 0.01f));
        _cfg.Subs(LightingCvars.Margin, v => _lightMargin = MathF.Max(v, 0f));
        _cfg.Subs(LightingCvars.MaxSize, v => _lightMaxSize = Math.Max(v, 16));
        _cfg.Subs(LightingCvars.Overbright, v => _overbright = MathF.Max(v, 1f));
        _cfg.Subs(LightingCvars.Dither, v => _lightDither = v ? 1f : 0f);
        _cfg.Subs(LightingCvars.Cache, v => _lightCache = v);
        _cfg.Subs(LightingCvars.StaticDelay, v => _lightStaticDelay = MathF.Max(v, 0f));

        // 0 is what turns the snap off in the shader
        _cfg.Subs(LightingCvars.Pixelated, _ => UpdatePixelSize());
        _cfg.Subs(LightingCvars.PixelSize, _ => UpdatePixelSize());
    }

    private void UpdatePixelSize()
        => _lightPixelSize = _cfg.Get(LightingCvars.Pixelated) ? MathF.Max(_cfg.Get(LightingCvars.PixelSize), 0.01f) : 0f;

    /// <summary>
    /// Draws, before any viewport, what the light maps of this frame are made of: once for every lit viewport.
    /// </summary>
    private void PrepareLighting()
    {
        _lightLayouts.Clear();
        _layoutList.Clear();

        if (!_lighting.Enabled || LightRenderer is not { } lights || GameClient.GameState != GameState.Running
            || _shaders.DefaultInstance is null)
            return;

        foreach (var viewport in _viewports)
        {
            if (viewport.Enabled)
                AddLayout(viewport);
        }

        AddLayout(MainViewport);

        if (_layoutList.Count == 0)
            return;

        var start = Stopwatch.GetTimestamp();
        _batcher.LightMap = null;

        var settings = new LightSettings(_lightResolution, _overbright, _lightSoftnessScale, _lightCache,
            _lightStaticDelay);
        lights.Prepare(_layoutList, settings, GameClient.Timing.TotalTime, _frameStats.Light);

        _frameStats.Light.PrepareMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    // what the light map of a viewport covers: its view and a margin, on the texel grid of the world
    private void AddLayout(RenderViewport viewport)
    {
        if (!viewport.LightingEnabled)
            return;

        viewport.Camera.CacheFrame();

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

        var layout = new LightMapLayout(new RectangleF(left, top, size.X / scale, size.Y / scale), size, scale);
        _lightLayouts[viewport] = layout;
        _layoutList.Add(layout);
    }

    /// <summary>
    /// Builds the light map of <paramref name="viewport"/> for this frame, before its world is drawn. 
    /// </summary>
    private bool BuildLightMap(RenderViewport viewport, float dt)
    {
        viewport.WasLit = false;

        if (!_lightLayouts.TryGetValue(viewport, out var layout) || LightRenderer is not { } lights)
            return false;

        var start = Stopwatch.GetTimestamp();
        var device = GameClient.GraphicsDevice;

        var target = viewport.EnsureLightTarget(device, layout.Size);

        // read by what was drawn before (the atlases, the last viewport)
        device.Textures[0] = null;
        device.Textures[1] = null;
        device.SetRenderTarget(target);

        var ambient = lights.GetAmbientColor().ToVector3() / _overbright;
        device.Clear(new Color(ambient.X, ambient.Y, ambient.Z, 1f));

        var bounds = layout.Bounds;
        var lightView = Matrix.CreateTranslation(-bounds.X, -bounds.Y, 0f)
                        * Matrix.CreateScale(layout.Scale, layout.Scale, 1f);

        lights.Composite(layout, lightView);

        // what the content adds to the light, in world space and adding up like the lights
        var blend = BlendState;
        BlendState = BlendState.Additive;
        _transformOverride = lightView;
        DrawOverlays(viewport, RenderStage.LightMap, dt);
        _transformOverride = null;
        BlendState = blend;

        viewport.LightMapBounds = bounds;
        viewport.LightMapSize = layout.Size;
        viewport.LightMapTransform = new Vector4(
            layout.Scale / target.Width, layout.Scale / target.Height,
            -bounds.X * layout.Scale / target.Width, -bounds.Y * layout.Scale / target.Height);
        viewport.WasLit = true;

        _frameStats.Light.LightMapTexels += layout.Size.X * layout.Size.Y;
        _frameStats.Light.LightMapMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
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
}
