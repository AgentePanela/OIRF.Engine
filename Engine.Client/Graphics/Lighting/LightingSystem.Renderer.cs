using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Engine.Client.Graphics.Batching;
using Engine.Client.Graphics.Shaders;
using Engine.Shared;
using Engine.Shared.Common;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/*
    God help whorever want to read this code.
*/

/// <summary>
/// What a light map covers this frame
/// </summary>
internal readonly record struct LightMapLayout(RectangleF Bounds, Point Size, float Scale);

internal readonly record struct LightSettings(float Scale, float Overbright, float SoftnessScale, bool Cache,
    float StaticDelay);

// Draws the lights, once per frame for every viewport, and keeps what did not change.
public sealed partial class LightingSystem
{
    [Dependency] private readonly ShaderManager _shaders = default!;
    [Dependency] private readonly SpriteLightSystem _spriteLights = default!;

    // the one of the render manager, taken at every Prepare
    private SpriteBatcher _batcher = default!;

    private readonly LightAtlas _staticAtlas = new();
    private readonly LightAtlas _dynamicAtlas = new();
    private readonly LightChunkCache _chunks = new();
    private readonly ShadowBatch _shadowBatch = new();

    private sealed class LightState
    {
        public LightDraw Light;

        public double ChangedAt;

        /// <summary>
        /// Static this frame.
        /// </summary>
        public bool IsStatic;

        public bool HasSquare;

        /// <summary>
        /// Its square holds it as it is now.
        /// </summary>
        public bool SquareDrawn;

        public bool Baked;

        public int Seen;

        public RectangleF Reach => new(Light.Center.X - Light.Radius, Light.Center.Y - Light.Radius,
            Light.Radius * 2f, Light.Radius * 2f);
    }

    private readonly Dictionary<EntityUid, LightState> _states = new();
    private readonly List<LightDraw> _collected = new();
    private readonly List<LightDraw> _dynamic = new();
    private readonly List<LightState> _toDraw = new();
    private readonly List<ShadowSegment> _segments = new();
    private readonly List<RectangleF> _changes = new();
    private readonly List<RectangleF> _areas = new();
    private readonly List<EntityUid> _gone = new();

    private readonly List<SpriteLightDraw> _spriteLightDraws = new();

    private int _frame;
    private bool _hasCache;
    private LightSettings _cacheSettings;
    private bool _chunksInUse;
    private bool _warnedFull;
    private float _overbright = 1f;

    long ILightRenderer.Bytes => _staticAtlas.Bytes + _dynamicAtlas.Bytes + _chunks.Bytes;

    private static readonly BlendState LightSquareBlend = new()
    {
        ColorSourceBlend = Blend.InverseDestinationAlpha,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
    };

    // squares and pieces added up: only their color, the alpha of a square holds its shadows
    private static readonly BlendState AddColorBlend = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
    };

    void ILightRenderer.Prepare(List<LightMapLayout> layouts, LightSettings settings, double now, LightStats stats)
    {
        ILightRenderer source = this;
        _batcher = ((RenderManager)_renderMan).Batcher;
        _frame++;
        _overbright = settings.Overbright;

        // what is kept was drawn for other settings
        if (!_hasCache || settings.Scale != _cacheSettings.Scale || settings.Overbright != _cacheSettings.Overbright
            || settings.Cache != _cacheSettings.Cache)
            ResetCache(settings);

        _areas.Clear();
        foreach (var layout in layouts)
            _areas.Add(layout.Bounds);

        // the lights of every piece that is kept, see LightChunkCache.Update
        var tracked = Grow(Union(_areas), LightChunkCache.ChunkSize * 2f);

        _spriteLightDraws.Clear();
        _spriteLights.CollectLights(Union(_areas), _spriteLightDraws);
        stats.SpriteLights = _spriteLightDraws.Count;

        // an occluder that changed changes the shadows of the lights that reach it
        _changes.Clear();
        source.TakeOccluderChanges(_changes);
        foreach (var change in _changes)
        {
            foreach (var state in _states.Values)
            {
                if (state.Light.CastShadows && Intersects(state.Reach, change))
                    Changed(state, now);
            }
        }

        _collected.Clear();
        source.CollectLights(tracked, settings.SoftnessScale, _collected);

        _dynamic.Clear();
        _toDraw.Clear();
        foreach (var light in _collected)
        {
            if (!_states.TryGetValue(light.Owner, out var state))
            {
                _states[light.Owner] = state = new LightState { Light = light, ChangedAt = now };
            }
            else if (!SameLight(state.Light, light))
            {
                Changed(state, now);

                // the square stays where it is, if it is still the right size
                var (page, region, slotScale) = (state.Light.Page, state.Light.Region, state.Light.SlotScale);
                state.Light = light;
                (state.Light.Page, state.Light.Region, state.Light.SlotScale) = (page, region, slotScale);
            }

            state.Seen = _frame;
            state.IsStatic = settings.Cache && !light.Animated && now - state.ChangedAt >= settings.StaticDelay;
            if (light.Animated)
                stats.AnimatedLights++;

            if (!state.IsStatic)
            {
                Unbake(state);
                _dynamic.Add(light);
                continue;
            }

            if (!state.SquareDrawn)
                _toDraw.Add(state);

            if (!state.Baked)
            {
                state.Baked = true;
                _chunks.MarkDirty(state.Reach);
            }
        }

        // gone, disabled, or out of reach of every viewport
        _gone.Clear();
        foreach (var (uid, state) in _states)
        {
            if (state.Seen == _frame)
                continue;

            Unbake(state);
            _gone.Add(uid);
        }

        foreach (var uid in _gone)
            _states.Remove(uid);

        var device = GameClient.GraphicsDevice;
        var time = (float)GameClient.Timing.TotalTime;

        DrawStaticSquares(source, device, settings, time, stats);

        _chunksInUse = settings.Cache && _chunks.Update(_areas, _frame);
        if (_chunksInUse)
            RebuildChunks(device, time, stats);

        DrawDynamic(source, device, settings, time, stats);

        stats.CacheEnabled = settings.Cache;
        stats.Lights += _collected.Count;
        stats.DynamicLights += _dynamic.Count;
        stats.StaticLights += _collected.Count - _dynamic.Count;
        stats.ChunksInUse = _chunksInUse;
        stats.ChunksResident = _chunks.Resident.Count;
        stats.ChunkCapacity = _chunks.Capacity;
        stats.StaticAtlasPages = _staticAtlas.PagesUsed;
        stats.DynamicAtlasPages = _dynamicAtlas.PagesUsed;
        stats.ShadowSegments += _shadowBatch.Count;
        stats.CacheBytes = source.Bytes;
        _shadowBatch.Count = 0;
    }

    internal void CollectPages(List<(string Name, Texture2D Page)> pages)
    {
        for (var i = 0; i < _staticAtlas.Pages.Count; i++)
            pages.Add(($"Static {i}", _staticAtlas.Pages[i]));

        for (var i = 0; i < _dynamicAtlas.Pages.Count; i++)
            pages.Add(($"Dynamic {i}", _dynamicAtlas.Pages[i]));

        for (var i = 0; i < _chunks.Pages.Count; i++)
            pages.Add(($"Chunks {i}", _chunks.Pages[i]));
    }

    void ILightRenderer.Composite(LightMapLayout layout, Matrix lightView)
    {
        var device = GameClient.GraphicsDevice;
        var bounds = layout.Bounds;

        _batcher.Begin(lightView, BlendState.Additive, (float)GameClient.Timing.TotalTime);
        _batcher.SetStencil(DepthStencilState.None, AddColorBlend);

        if (_chunksInUse)
        {
            foreach (var chunk in _chunks.Resident)
            {
                var world = LightChunkCache.WorldRect(chunk.Coords);
                if (!Intersects(world, bounds))
                    continue;

                var (page, square) = _chunks.Locate(chunk.Slot);
                var inner = new Rectangle(square.X + LightChunkCache.Border, square.Y + LightChunkCache.Border,
                    _chunks.ChunkTexels, _chunks.ChunkTexels);

                _batcher.SetState(null, SamplerState.LinearClamp);
                _batcher.Draw(_chunks.GetPage(device, page), new Vector2(world.X, world.Y), inner, Color.White, 0f,
                    Vector2.Zero, new Vector2(LightChunkCache.ChunkSize / _chunks.ChunkTexels), SpriteEffects.None);
            }
        }
        else
        {
            // too many pieces for the view: the static lights one by one, straight from their squares
            foreach (var state in _states.Values)
            {
                if (state.Baked && Intersects(state.Reach, bounds))
                    DrawSquare(_staticAtlas.GetPage(device, state.Light.Page), state.Light);
            }
        }

        foreach (ref readonly var light in CollectionsMarshal.AsSpan(_dynamic))
        {
            if (light.Page >= 0 && Intersects(Reach(light), bounds))
                DrawSquare(_dynamicAtlas.GetPage(device, light.Page), light);
        }

        DrawSpriteLights(bounds);

        _batcher.Flush();
        _batcher.SetStencil(DepthStencilState.None, null);
    }

    private void DrawSpriteLights(RectangleF bounds)
    {
        if (_spriteLightDraws.Count == 0 || _shaders.GlowInstance is not { } glow)
            return;

        foreach (ref readonly var light in CollectionsMarshal.AsSpan(_spriteLightDraws))
        {
            if (!SpriteLightSystem.Reaches(light, bounds))
                continue;

            var texture = light.Texture;
            var region = light.TexRegion;
            var frame = new Vector2(region.Width, region.Height);

            _batcher.SetState(glow, SamplerState.PointClamp);
            _batcher.Custom0 = new Vector4((float)region.X / texture.Width, (float)region.Y / texture.Height,
                (float)region.Width / texture.Width, (float)region.Height / texture.Height);
            _batcher.Custom1 = new Vector4(light.Spread / light.Size.X, light.Spread / light.Size.Y,
                light.Intensity * _lighting.LightIntensity / _overbright, 0f);
            _batcher.Lighting = default;
            _batcher.Draw(texture, light.Center, region, light.Color, light.Rotation, frame / 2f, light.Size / frame,
                SpriteEffects.None);
        }

        _batcher.Custom0 = default;
        _batcher.Custom1 = default;
    }

    // a light's square, over the world where the light is
    private void DrawSquare(Texture2D page, in LightDraw light)
    {
        _batcher.SetState(null, SamplerState.LinearClamp);
        _batcher.Draw(page, light.Center, light.Region, Color.White, 0f,
            new Vector2(light.Region.Width / 2f, light.Region.Height / 2f), new Vector2(1f / light.SlotScale),
            SpriteEffects.None);
    }

    private void DrawStaticSquares(ILightRenderer source, GraphicsDevice device, LightSettings settings, float time,
        LightStats stats)
    {
        if (_toDraw.Count == 0)
            return;

        if (!PlaceStatic(settings))
        {
            // the atlas filled up with squares of lights long gone: start it over with the ones there are now
            _staticAtlas.Reset();
            _toDraw.Clear();
            foreach (var state in _states.Values)
            {
                state.HasSquare = false;
                state.SquareDrawn = false;
                if (state.Seen == _frame && state.IsStatic)
                    _toDraw.Add(state);
            }

            if (!PlaceStatic(settings) && !_warnedFull)
            {
                Log.Warn($"The static light atlas is full ({LightAtlas.MaxPages} pages): some lights are drawn every frame");
                _warnedFull = true;
            }
        }

        // no room even then: drawn every frame like a dynamic light, until there is
        foreach (var state in _toDraw)
        {
            if (state.HasSquare)
                continue;

            state.IsStatic = false;
            Unbake(state);
            _dynamic.Add(state.Light);
        }

        if (_shaders.LightInstance is not { } lightShader)
            return;

        var shadowEffect = _shaders.GetShader("LightShadow");

        for (var page = 0; page < _staticAtlas.PagesUsed; page++)
        {
            var any = false;
            foreach (var state in _toDraw)
            {
                if (state.HasSquare && state.Light.Page == page)
                {
                    any = true;
                    break;
                }
            }

            if (!any)
                continue;

            UnbindLightTextures(device);
            device.SetRenderTarget(_staticAtlas.GetPage(device, page));

            // only its squares are cleared, the rest of the page holds lights that did not change
            _batcher.Begin(Matrix.Identity, BlendState.Opaque, time);
            foreach (var state in _toDraw)
            {
                if (state.HasSquare && state.Light.Page == page)
                {
                    _batcher.SetState(null, SamplerState.PointClamp);
                    _batcher.Draw(_batcher.White, state.Light.Region, new Rectangle(0, 0, 1, 1), Color.Transparent);
                }
            }

            _batcher.Flush();

            if (shadowEffect is not null)
            {
                foreach (var state in _toDraw)
                {
                    if (state.HasSquare && state.Light.Page == page && state.Light.CastShadows)
                        AddShadows(source, device, shadowEffect, state.Light);
                }

                _shadowBatch.Flush(device, shadowEffect);
            }

            _batcher.Begin(Matrix.Identity, BlendState.Additive, time);
            _batcher.SetStencil(DepthStencilState.None, LightSquareBlend);
            foreach (var state in _toDraw)
            {
                if (!state.HasSquare || state.Light.Page != page)
                    continue;

                DrawLightInSquare(lightShader, state.Light);
                state.SquareDrawn = true;
                stats.LightsDrawn++;
            }

            _batcher.Flush();
            _batcher.SetStencil(DepthStencilState.None, null);

            if (shadowEffect is not null)
            {
                _shadowBatch.SetSeeThrough();
                foreach (var state in _toDraw)
                {
                    if (state.HasSquare && state.Light.Page == page && state.Light.CastShadows)
                        AddSeeThrough(source, device, shadowEffect, state.Light);
                }

                _shadowBatch.Flush(device, shadowEffect);
            }
        }
    }

    // gives a square to every light to draw that has none (or one of the wrong size)
    private bool PlaceStatic(LightSettings settings)
    {
        _toDraw.Sort(static (a, b) => b.Light.Radius.CompareTo(a.Light.Radius));

        var fits = true;
        foreach (var state in _toDraw)
        {
            var size = LightAtlas.RegionSize(state.Light.Radius, settings.Scale, out var slotScale);
            state.Light.SlotScale = slotScale;

            if (state.HasSquare && state.Light.Region.Width == size)
                continue;

            if (_staticAtlas.TryPlace(size, out state.Light.Page, out var at))
            {
                state.Light.Region = new Rectangle(at.X, at.Y, size, size);
                state.HasSquare = true;
                continue;
            }

            state.HasSquare = false;
            fits = false;
        }

        return fits;
    }

    private void RebuildChunks(GraphicsDevice device, float time, LightStats stats)
    {
        var texelsPerUnit = _chunks.ChunkTexels / LightChunkCache.ChunkSize;
        var border = LightChunkCache.Border / texelsPerUnit;
        var target = -1;

        foreach (var chunk in _chunks.Resident)
        {
            if (!chunk.Dirty)
                continue;

            var (page, square) = _chunks.Locate(chunk.Slot);
            if (page != target)
            {
                UnbindLightTextures(device);
                device.SetRenderTarget(_chunks.GetPage(device, page));
                target = page;
            }

            var world = LightChunkCache.WorldRect(chunk.Coords);
            var view = Matrix.CreateTranslation(-world.X, -world.Y, 0f)
                       * Matrix.CreateScale(texelsPerUnit, texelsPerUnit, 1f)
                       * Matrix.CreateTranslation(square.X + LightChunkCache.Border, square.Y + LightChunkCache.Border, 0f);

            _batcher.Begin(view, BlendState.Opaque, time);
            _batcher.SetScissor(square);

            // emptied, its border too
            _batcher.SetState(null, SamplerState.PointClamp);
            _batcher.Draw(_batcher.White, new Vector2(world.X - border, world.Y - border), new Rectangle(0, 0, 1, 1),
                Color.Black, 0f, Vector2.Zero, new Vector2(square.Width / texelsPerUnit), SpriteEffects.None);
            _batcher.Flush();

            // the static lights over it, from their squares
            var reach = Grow(world, border);
            _batcher.SetStencil(DepthStencilState.None, AddColorBlend);
            foreach (var state in _states.Values)
            {
                if (state.Baked && Intersects(state.Reach, reach))
                    DrawSquare(_staticAtlas.GetPage(device, state.Light.Page), state.Light);
            }

            _batcher.Flush();
            _batcher.SetStencil(DepthStencilState.None, null);
            _batcher.SetScissor(null);

            chunk.Dirty = false;
            stats.ChunksRebuilt++;
        }
    }

    private void DrawDynamic(ILightRenderer source, GraphicsDevice device, LightSettings settings, float time,
        LightStats stats)
    {
        _dynamicAtlas.Reset();
        if (_dynamic.Count == 0)
            return;

        var span = CollectionsMarshal.AsSpan(_dynamic);
        for (var i = 0; i < span.Length; i++)
        {
            var size = LightAtlas.RegionSize(span[i].Radius, settings.Scale, out span[i].SlotScale);
            span[i].Region = new Rectangle(0, 0, size, size);
        }

        // the biggest first, they pack better
        _dynamic.Sort(static (a, b) => b.Region.Width.CompareTo(a.Region.Width));
        span = CollectionsMarshal.AsSpan(_dynamic);

        for (var i = 0; i < span.Length; i++)
        {
            if (_dynamicAtlas.TryPlace(span[i].Region.Width, out span[i].Page, out var at))
            {
                span[i].Region.Location = at;
                continue;
            }

            if (!_warnedFull)
            {
                Log.Warn($"The light atlas is full ({LightAtlas.MaxPages} pages): lights past it are not drawn");
                _warnedFull = true;
            }
        }

        if (_shaders.LightInstance is not { } lightShader)
            return;

        var shadowEffect = _shaders.GetShader("LightShadow");

        for (var page = 0; page < _dynamicAtlas.PagesUsed; page++)
        {
            UnbindLightTextures(device);
            device.SetRenderTarget(_dynamicAtlas.GetPage(device, page));
            device.Clear(Color.Transparent);

            if (shadowEffect is not null)
            {
                foreach (ref readonly var light in span)
                {
                    if (light.Page == page && light.CastShadows)
                        AddShadows(source, device, shadowEffect, light);
                }

                _shadowBatch.Flush(device, shadowEffect);
            }

            // in page pixels
            _batcher.Begin(Matrix.Identity, BlendState.Additive, time);
            _batcher.SetStencil(DepthStencilState.None, LightSquareBlend);
            foreach (ref readonly var light in span)
            {
                if (light.Page != page)
                    continue;

                DrawLightInSquare(lightShader, light);
                stats.LightsDrawn++;
            }

            _batcher.Flush();
            _batcher.SetStencil(DepthStencilState.None, null);

            if (shadowEffect is not null)
            {
                _shadowBatch.SetSeeThrough();
                foreach (ref readonly var light in span)
                {
                    if (light.Page == page && light.CastShadows)
                        AddSeeThrough(source, device, shadowEffect, light);
                }

                _shadowBatch.Flush(device, shadowEffect);
            }
        }
    }

    private void AddShadows(ILightRenderer source, GraphicsDevice device, Effect shadowEffect, in LightDraw light)
    {
        _segments.Clear();
        source.CollectShadowSegments(light, _segments);
        foreach (var segment in _segments)
            _shadowBatch.Add(device, shadowEffect, segment, light);
    }

    private void AddSeeThrough(ILightRenderer source, GraphicsDevice device, Effect shadowEffect, in LightDraw light)
    {
        _segments.Clear();
        source.CollectSeeThroughSegments(light, _segments);
        foreach (var segment in _segments)
            _shadowBatch.Add(device, shadowEffect, segment, light);
    }

    // one light, filling its square: 2 radius wide at the square scale, turned with it
    private void DrawLightInSquare(ShaderInstance shader, in LightDraw light)
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
    }

    private void ResetCache(LightSettings settings)
    {
        _states.Clear();
        _staticAtlas.Reset();
        _chunks.Reset(settings.Scale);
        _cacheSettings = settings;
        _hasCache = true;
    }

    // it changed: it is dynamic for a while, and has to be drawn again once it settles
    private void Changed(LightState state, double now)
    {
        state.ChangedAt = now;
        state.SquareDrawn = false;
        Unbake(state);
    }

    // taken out of the pieces it was summed into
    private void Unbake(LightState state)
    {
        if (!state.Baked)
            return;

        state.Baked = false;
        _chunks.MarkDirty(state.Reach);
    }

    // a target about to be drawn into cannot still be bound as a texture
    private static void UnbindLightTextures(GraphicsDevice device)
    {
        device.Textures[0] = null;
        device.Textures[1] = null;
    }

    private static bool SameLight(in LightDraw a, in LightDraw b)
        => a.Center == b.Center && a.Radius == b.Radius && a.Rotation == b.Rotation && a.Color == b.Color
           && a.Brightness == b.Brightness && a.HalfCone == b.HalfCone && a.ConeSoftness == b.ConeSoftness
           && a.Falloff == b.Falloff && a.Curve == b.Curve && ReferenceEquals(a.Mask, b.Mask)
           && a.MaskRegion == b.MaskRegion && a.CastShadows == b.CastShadows && a.SourceRadius == b.SourceRadius;

    private static RectangleF Reach(in LightDraw light)
        => new(light.Center.X - light.Radius, light.Center.Y - light.Radius, light.Radius * 2f, light.Radius * 2f);

    private static RectangleF Union(List<RectangleF> areas)
    {
        var left = float.MaxValue;
        var top = float.MaxValue;
        var right = float.MinValue;
        var bottom = float.MinValue;
        foreach (var area in areas)
        {
            left = MathF.Min(left, area.Left);
            top = MathF.Min(top, area.Top);
            right = MathF.Max(right, area.Right);
            bottom = MathF.Max(bottom, area.Bottom);
        }

        return new RectangleF(left, top, right - left, bottom - top);
    }

    private static RectangleF Grow(RectangleF area, float by)
        => new(area.X - by, area.Y - by, area.Width + by * 2f, area.Height + by * 2f);

    private static bool Intersects(RectangleF a, RectangleF b)
        => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    private void DisposeRenderer()
    {
        // drawn again from nothing if it is used after this
        _states.Clear();
        _hasCache = false;

        _staticAtlas.Dispose();
        _dynamicAtlas.Dispose();
        _chunks.Dispose();
    }
}
