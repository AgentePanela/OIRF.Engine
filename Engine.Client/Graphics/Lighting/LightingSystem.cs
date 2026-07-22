using System.Collections.Generic;
using System.Diagnostics;
using System;
using Engine.Client.Assets;
using Engine.Client.Graphics.Shaders;
using Engine.Client.Tilemap;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Components.Lighting;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Builds the lightmap every frame: shadow map, light pass, optional wall
/// bleed and blur. <see cref="ApplyAfterWorld"/> then multiplies the result
/// over the rendered scene.
/// </summary>
public sealed class LightingSystem : EntityDrawSystem
{
    [Dependency] private readonly LightingManager _lighting = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly RenderManager _render = default!;
    [Dependency] private readonly Camera2D _camera = default!;
    [Dependency] private readonly ShaderManager _shaders = default!;
    [Dependency] private readonly IAssetManager _assets = default!;
    [Dependency] private readonly ViewportAdapter _viewport = default!;
    [Dependency] private readonly LightOcclusionSystem _occlusionSys = default!;

    private readonly LightingRenderTarget _lightmap = new();
    private readonly ShadowMapRT _shadowMap = new();
    private readonly WallBleedRT _wallBleed = new();
    private readonly ScratchRT _blurScratch = new();

    // occluder edge geometry, built once per frame and drawn for every
    // shadow light. Capped at 4096 occluders by the 16 bit index range
    private const int MaxOccluderCap = 4096;
    private int _occluderCapacity = 256;
    private ShadowGeometry.OccluderVertex[] _shadowVerts = new ShadowGeometry.OccluderVertex[256 * 16];
    private DynamicVertexBuffer? _shadowVB;
    private IndexBuffer? _shadowIB;
    private int _shadowTriCount;

    private readonly List<Rectangle> _scratchTileRects = new();

    // reused every frame to avoid allocations
    private readonly List<LightEntry> _lights = new();
    private readonly List<(Rectangle Bounds, TransformComponent Transform)> _occluders = new();
    private DiskVertex[] _wallVerts = new DiskVertex[256 * 6];

    private float _maxLightRadius;
    private bool _warnedShadowCap;

    // quad covering a 2r x 2r square around a light, in world space
    private struct DiskVertex
    {
        public Vector2 WorldPos;
        public const int SizeInBytes = 8;
        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0));
    }
    private DiskVertex[] _diskQuad = new DiskVertex[6];

    // fullscreen quad in clip space, for the post-process passes
    private struct ScreenVertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
        public const int SizeInBytes = 16;
        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));
    }
    private static readonly ScreenVertex[] ScreenQuad =
    {
        new() { Position = new Vector2(-1, -1), TexCoord = new Vector2(0, 0) },
        new() { Position = new Vector2( 1, -1), TexCoord = new Vector2(1, 0) },
        new() { Position = new Vector2( 1,  1), TexCoord = new Vector2(1, 1) },
        new() { Position = new Vector2(-1, -1), TexCoord = new Vector2(0, 0) },
        new() { Position = new Vector2( 1,  1), TexCoord = new Vector2(1, 1) },
        new() { Position = new Vector2(-1,  1), TexCoord = new Vector2(0, 1) },
    };

    // plain additive blend, lights output premultiplied rgb + strength in alpha
    private static readonly BlendState AdditivePremultiplied = new BlendState
    {
        ColorSourceBlend      = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend      = Blend.One,
        AlphaDestinationBlend = Blend.One,
        ColorBlendFunction    = BlendFunction.Add,
        AlphaBlendFunction    = BlendFunction.Add,
    };

    // CullMode.None because the camera transform can flip the quad winding
    private static readonly RasterizerState ScissorRasterizer = new RasterizerState
    {
        CullMode = CullMode.None,
        ScissorTestEnable = true,
    };

    // LESS so overlapping occluder slices keep the closest distance
    private static readonly DepthStencilState ShadowDepthState = new DepthStencilState
    {
        DepthBufferEnable       = true,
        DepthBufferWriteEnable  = true,
        DepthBufferFunction     = CompareFunction.Less,
    };

    private Effect? _shadowDepthEffect;
    private Effect? _lightSoftEffect;
    private Effect? _lightBlurEffect;
    private Effect? _wallMergeEffect;

    // parameter lookups by name are dictionary hits, cache them once
    private EffectParameter? _sdLightPos, _sdLightRadius, _sdWrapPass;
    private EffectParameter? _lpViewProj, _lpShadowMap, _lpShadowMapTexel,
        _lpCenter, _lpColor, _lpRange, _lpPower, _lpSoftness, _lpFalloff,
        _lpCurveFactor, _lpIndex, _lpContactBias,
        _lpDirection, _lpConeAngle, _lpConeSoftness;
    private EffectParameter? _blSourceMap, _blSourceTexel, _blIsHorizontal;
    private EffectParameter? _wmBlurred, _wmViewProj;

    private readonly Stopwatch _passTimer = new();
    private readonly Stopwatch _frameTimer = new();

    public override void Init()
    {
        base.Init();

        _cfg.Subs(LightingCvars.LightmapScale,    v => _lighting.LightmapScale    = v);
        _cfg.Subs(LightingCvars.PixelatedLighting, v => _lighting.PixelatedLighting = v);
        _cfg.Subs(LightingCvars.LightPixelSize,   v => _lighting.LightPixelSize   = v);

        _shadowDepthEffect = _shaders.GetShader("ShadowDepth")?.Clone();
        _lightSoftEffect   = _shaders.GetShader("LightSoft")?.Clone();
        _lightBlurEffect   = _shaders.GetShader("LightBlur")?.Clone();
        _wallMergeEffect   = _shaders.GetShader("WallMerge")?.Clone();

        if (_shadowDepthEffect is null)
            Log.Warn("ShadowDepth.fx not found - shadows will be disabled.");
        if (_lightSoftEffect is null)
            Log.Warn("LightSoft.fx not found - point/spot lights will not render.");
        if (_wallMergeEffect is null)
            Log.Warn("WallMerge.fx not found - wall bleed will be disabled.");

        CacheEffectParameters();
    }

    private void CacheEffectParameters()
    {
        if (_shadowDepthEffect is not null)
        {
            var p = _shadowDepthEffect.Parameters;
            _sdLightPos    = p["lightPos"];
            _sdLightRadius = p["lightRadius"];
            _sdWrapPass    = p["shadowWrapPass"];
        }

        if (_lightSoftEffect is not null)
        {
            var p = _lightSoftEffect.Parameters;
            _lpViewProj       = p["viewProj"];
            _lpShadowMap      = p["ShadowMap"];
            _lpShadowMapTexel = p["shadowMapTexel"];
            _lpCenter         = p["lightCenter"];
            _lpColor          = p["lightColor"];
            _lpRange          = p["lightRange"];
            _lpPower          = p["lightPower"];
            _lpSoftness       = p["lightSoftness"];
            _lpFalloff        = p["lightFalloff"];
            _lpCurveFactor    = p["lightCurveFactor"];
            _lpIndex          = p["lightIndex"];
            _lpContactBias    = p["shadowContactBias"];
            _lpDirection      = p["lightDirection"];
            _lpConeAngle      = p["lightConeAngle"];
            _lpConeSoftness   = p["lightConeSoftness"];
        }

        if (_lightBlurEffect is not null)
        {
            var p = _lightBlurEffect.Parameters;
            _blSourceMap    = p["SourceMap"];
            _blSourceTexel  = p["SourceTexel"];
            _blIsHorizontal = p["isHorizontal"];
        }

        if (_wallMergeEffect is not null)
        {
            var p = _wallMergeEffect.Parameters;
            _wmBlurred  = p["BlurredLightMap"];
            _wmViewProj = p["viewProj"];
        }
    }

    public override void Draw(float dt)
    {
        if (!_lighting.Enabled)
            return;

        BuildLightmap();
    }

    /// <summary>
    /// Multiplies the lightmap onto the scene and blits the result to
    /// <see cref="RenderManager.FinalTarget"/>/the backbuffer. Call after the
    /// world has been drawn to <see cref="RenderManager.SceneTarget"/>.
    /// </summary>
    public void ApplyAfterWorld()
    {
        if (!_lighting.Enabled)
            return;

        var scene = _render.SceneTarget;
        if (scene is null || _lightmap.Target is null)
            return;

        if (_lighting.DebugDraw)
        {
            GameClient.GraphicsDevice.SetRenderTarget(_render.FinalTarget);
            GameClient.GraphicsDevice.Viewport = _render.LastBackbufferViewport;

            // SpriteBatch coords are viewport relative, so draw at 0,0 -
            // the letterbox offset is already applied by the viewport
            _render.DrawFullscreenQuad(_lightmap.Target, BlendState.Opaque, SamplerState.PointClamp);
            return;
        }

        // Multiply the lightmap onto SceneTarget in place. StencilTestShadedOnly
        // only lets the blend touch pixels stamped "shaded" (0) by DrawQueue, so
        // unshaded pixels (stencil 1) are left untouched at full brightness.
        // SceneTarget still holds its stencil contents from DrawQueue
        // (RenderTargetUsage.PreserveContents).
        GameClient.GraphicsDevice.SetRenderTarget(scene);
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, scene.Width, scene.Height);

        var lightSampler = _lighting.PixelatedLighting ? SamplerState.PointClamp : SamplerState.LinearClamp;
        _render.DrawFullscreenQuad(_lightmap.Target, RenderManager.LightMultiplyBlend, lightSampler, RenderManager.StencilTestShadedOnly);

        // Blit the now fully-lit SceneTarget onto FinalTarget/the backbuffer.
        GameClient.GraphicsDevice.SetRenderTarget(_render.FinalTarget);
        GameClient.GraphicsDevice.Viewport = _render.LastBackbufferViewport;
        _render.DrawFullscreenQuad(scene, BlendState.Opaque, SamplerState.PointClamp);
    }

    public override void OnShutdown()
    {
        base.OnShutdown();
        _shadowMap.Dispose();
        _wallBleed.Dispose();
        _blurScratch.Dispose();
        _shadowVB?.Dispose(); _shadowVB = null;
        _shadowIB?.Dispose(); _shadowIB = null;
    }

    private void BuildLightmap()
    {
        _frameTimer.Restart();
        double shadowPassMs = 0, lightPassMs = 0, wallBleedMs = 0, lightBlurMs = 0;

        // highest priority AmbientLightComponent wins, fallback is the manager default
        var ambientColor = _lighting.AmbientLight;
        var ambientIntensity = 1f;
        AmbientLightComponent? bestAmbient = null;
        foreach (var (_, ambient) in GetEntitiesWithComp<AmbientLightComponent>())
            if (bestAmbient is null || ambient.Priority > bestAmbient.Priority)
                bestAmbient = ambient;
        if (bestAmbient is not null)
        {
            ambientColor = bestAmbient.Color;
            ambientIntensity = bestAmbient.Intensity;
        }
        var baseAmbient = ambientColor * ambientIntensity;

        int lightW, lightH;
        if (_lighting.PixelatedLighting)
        {
            int ps = _lighting.LightPixelSize;
            lightW = Math.Max(1, (_viewport.VirtualWidth  + ps - 1) / ps);
            lightH = Math.Max(1, (_viewport.VirtualHeight + ps - 1) / ps);
        }
        else
        {
            float scale = _lighting.LightmapScale;
            lightW = Math.Max(1, (int)(_viewport.VirtualWidth  * scale));
            lightH = Math.Max(1, (int)(_viewport.VirtualHeight * scale));
        }
        _lightmap.EnsureSize(lightW, lightH);
        if (_lightmap.Target is null)
            return;

        int shadowW = _lighting.ShadowMapSize;
        int shadowH = _lighting.MaxShadowcastingLights;
        _shadowMap.EnsureSize(shadowW, shadowH);
        if (_shadowDepthEffect is not null && _shadowMap.Target is null)
            return;

        // blur targets only exist while their feature is on
        bool wallBleed = _lighting.WallBleedEnabled && _wallMergeEffect is not null && _lightBlurEffect is not null;
        bool lightBlur = _lighting.LightBlurEnabled && _lightBlurEffect is not null;

        if (wallBleed)
        {
            // the bleed blur runs at half res, it's a low frequency glow
            _wallBleed.EnsureSize(Math.Max(1, lightW / 2), Math.Max(1, lightH / 2));
            wallBleed = _wallBleed.A is not null && _wallBleed.B is not null;
        }
        else
        {
            _wallBleed.Dispose();
        }

        if (lightBlur)
        {
            _blurScratch.EnsureSize(lightW, lightH);
            lightBlur = _blurScratch.Target is not null;
        }
        else
        {
            _blurScratch.Dispose();
        }

        CollectLights();
        CollectOccluders();
        int shadowLightCount = CountShadowLights();

        // shared by every pass that rasterizes world-space quads
        var viewProj = _camera.GetViewMatrix() * Matrix.CreateOrthographicOffCenter(
            0, _viewport.VirtualWidth, _viewport.VirtualHeight, 0, -1, 1);

        if (_shadowDepthEffect is not null && _shadowMap.Target is not null)
        {
            _passTimer.Restart();
            RenderShadowMap();
            _passTimer.Stop();
            shadowPassMs = _passTimer.Elapsed.TotalMilliseconds;
        }

        _passTimer.Restart();
        _render.BeginSceneRender(_lightmap.Target);
        GameClient.GraphicsDevice.Clear(baseAmbient);
        DrawRadialLights(viewProj);
        DrawTextureLights();
        _render.EndSceneRender();
        _passTimer.Stop();
        lightPassMs = _passTimer.Elapsed.TotalMilliseconds;

        if (wallBleed && _occluders.Count > 0)
        {
            _passTimer.Restart();
            RunWallBleed(viewProj);
            _passTimer.Stop();
            wallBleedMs = _passTimer.Elapsed.TotalMilliseconds;
        }

        if (lightBlur)
        {
            _passTimer.Restart();
            RunLightBlur();
            _passTimer.Stop();
            lightBlurMs = _passTimer.Elapsed.TotalMilliseconds;
        }

        _frameTimer.Stop();
        _lighting.RecordFrameStats(
            _lights.Count, shadowLightCount, _occluders.Count,
            shadowW, shadowH,
            _frameTimer.Elapsed.TotalMilliseconds,
            shadowPassMs, lightPassMs, wallBleedMs, lightBlurMs);
    }

    private struct LightEntry
    {
        public EntityUid Uid;
        public IRadialLight Comp;
        public Vector2 WorldPos;
        public bool CastShadows;
        public float DistSq;        // squared distance to the camera center
        public bool IsSpot;
        public float Direction;     // radians, spot only
        public float ConeHalfAngle; // radians, spot only
        public float ConeSoftness;  // radians, spot only
    }

    // shadow casters go last so the MaxLights cut drops them first
    private static readonly Comparison<LightEntry> ShadowsLastThenNearest = static (a, b) =>
    {
        int sgn = a.CastShadows.CompareTo(b.CastShadows);
        return sgn != 0 ? sgn : a.DistSq.CompareTo(b.DistSq);
    };

    private void CollectLights()
    {
        _lights.Clear();
        _maxLightRadius = 0f;
        var cam = _camera.WorldCenter;

        foreach (var (uid, point, transform) in GetEntitiesWithComp<PointLightComponent, TransformComponent>())
        {
            var worldPos = transform.Position + point.Offset;
            if (!_camera.IsOnScreen(worldPos, new Vector2(point.Radius * 2f)))
                continue;

            _maxLightRadius = MathF.Max(_maxLightRadius, point.Radius);
            _lights.Add(new LightEntry
            {
                Uid = uid,
                Comp = point,
                WorldPos = worldPos,
                CastShadows = point.CastShadows,
                DistSq = (worldPos - cam).LengthSquared(),
            });
        }

        foreach (var (uid, spot, transform) in GetEntitiesWithComp<SpotLightComponent, TransformComponent>())
        {
            var worldPos = transform.Position + spot.Offset;
            if (!_camera.IsOnScreen(worldPos, new Vector2(spot.Radius * 2f)))
                continue;

            float direction = (spot.RotatesWithTransform ? transform.Angle : 0f) + spot.Direction;
            float halfAngle = MathHelper.ToRadians(spot.ConeAngle) * 0.5f;
            float softness = MathHelper.Clamp(halfAngle * 0.25f, 0.01f, halfAngle);

            _maxLightRadius = MathF.Max(_maxLightRadius, spot.Radius);
            _lights.Add(new LightEntry
            {
                Uid = uid,
                Comp = spot,
                WorldPos = worldPos,
                CastShadows = spot.CastShadows,
                DistSq = (worldPos - cam).LengthSquared(),
                IsSpot = true,
                Direction = direction,
                ConeHalfAngle = halfAngle,
                ConeSoftness = softness,
            });
        }

        _lights.Sort(ShadowsLastThenNearest);

        if (_lights.Count > _lighting.MaxLights)
            _lights.RemoveRange(_lighting.MaxLights, _lights.Count - _lighting.MaxLights);
    }

    private int CountShadowLights()
    {
        int count = 0;
        foreach (var entry in _lights)
        {
            if (!entry.CastShadows) continue;
            if (++count >= _lighting.MaxShadowcastingLights) return count;
        }
        return count;
    }

    private void CollectOccluders()
    {
        _occluders.Clear();

        // an occluder within a light radius of the view can still push a
        // shadow into the view, so pad the culling bounds by the biggest one
        var bounds = _camera.ViewportBounds;
        int pad = (int)MathF.Ceiling(_maxLightRadius);
        bounds.Inflate(pad, pad);

        foreach (var (uid, occluder, transform) in GetEntitiesWithComp<OccluderComponent, TransformComponent>())
        {
            var b = _occlusionSys.GetOccluderBounds(uid, occluder, transform, _entManager);
            if (b.Width <= 0 || b.Height <= 0) continue;
            if (!bounds.Intersects(b)) continue;
            _occluders.Add((b, transform));
        }

        var tilemapSys = _entManager.GetSystem<TilemapSystem>();
        if (tilemapSys is not null)
        {
            foreach (var (_, tilemap, tmTransform) in GetEntitiesWithComp<TilemapComponent, TransformComponent>())
            {
                tilemapSys.GetSolidTilesInArea(tilemap, tmTransform, bounds, _scratchTileRects);
                foreach (var rect in _scratchTileRects)
                {
                    if (rect.Width <= 0 || rect.Height <= 0) continue;
                    _occluders.Add((rect, tmTransform));
                }
            }
            _scratchTileRects.Clear();
        }
    }

    private void RenderShadowMap()
    {
        if (!_shadowMap.Usable) return;

        var shadowMap = _shadowMap.Target!;
        var depthEffect = _shadowDepthEffect!;

        BuildShadowGeometry();

        // clear color = "no occluder", depth = far so the LESS test accepts the first write
        GameClient.GraphicsDevice.SetRenderTarget(shadowMap);
        GameClient.GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer,
            new Color(255, 255, 0, 255), 1f, 0);

        if (_shadowTriCount == 0 || _shadowVB is null || _shadowIB is null)
            return;

        var prevBlend = GameClient.GraphicsDevice.BlendState;
        var prevDepth = GameClient.GraphicsDevice.DepthStencilState;
        GameClient.GraphicsDevice.BlendState = BlendState.Opaque;
        GameClient.GraphicsDevice.DepthStencilState = ShadowDepthState;
        GameClient.GraphicsDevice.SetVertexBuffer(_shadowVB);
        GameClient.GraphicsDevice.Indices = _shadowIB;

        int shadowIdx = 0;
        foreach (var entry in _lights)
        {
            if (!entry.CastShadows) continue;
            if (shadowIdx >= _lighting.MaxShadowcastingLights)
            {
                if (!_warnedShadowCap)
                {
                    Log.Warn(
                        $"LightingSystem: shadow cap ({_lighting.MaxShadowcastingLights}) reached, " +
                        "extra lights render without shadows. Raise MaxShadowcastingLights or cull more lights.");
                    _warnedShadowCap = true;
                }
                continue;
            }

            GameClient.GraphicsDevice.Viewport = new Viewport(0, shadowIdx, shadowMap.Width, 1);
            _sdLightPos?.SetValue(entry.WorldPos);
            _sdLightRadius?.SetValue(entry.Comp.Radius);

            // pass 0 = normal range, pass 1 = tail that wraps around the +-pi seam
            for (int wrapPass = 0; wrapPass < 2; wrapPass++)
            {
                _sdWrapPass?.SetValue((float)wrapPass);
                foreach (var pass in depthEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    GameClient.GraphicsDevice.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList, 0, 0, _shadowTriCount);
                }
            }

            shadowIdx++;
        }

        GameClient.GraphicsDevice.SetVertexBuffer(null);
        GameClient.GraphicsDevice.Indices = null;
        GameClient.GraphicsDevice.BlendState = prevBlend;
        GameClient.GraphicsDevice.DepthStencilState = prevDepth;
    }

    // uploads the frame's occluder geometry to the shared vertex buffer.
    // The geometry doesn't depend on the light (lightPos is a uniform), so
    // every shadow light draws the same buffer
    private void BuildShadowGeometry()
    {
        _shadowTriCount = 0;

        int count = Math.Min(_occluders.Count, MaxOccluderCap);
        if (count == 0) return;

        if (count > _occluderCapacity || _shadowVB is null || _shadowIB is null)
        {
            while (_occluderCapacity < count)
                _occluderCapacity *= 2;
            _occluderCapacity = Math.Min(_occluderCapacity, MaxOccluderCap);

            if (_shadowVerts.Length < _occluderCapacity * 16)
                _shadowVerts = new ShadowGeometry.OccluderVertex[_occluderCapacity * 16];

            _shadowVB?.Dispose();
            _shadowIB?.Dispose();
            _shadowVB = new DynamicVertexBuffer(GameClient.GraphicsDevice,
                ShadowGeometry.OccluderVertex.Declaration, _occluderCapacity * 16, BufferUsage.WriteOnly);
            _shadowIB = new IndexBuffer(GameClient.GraphicsDevice,
                IndexElementSize.SixteenBits, _occluderCapacity * 24, BufferUsage.WriteOnly);
            _shadowIB.SetData(BuildQuadIndices(_occluderCapacity * 4));
        }

        int vertexCount = ShadowGeometry.Build(_occluders, _shadowVerts);
        if (vertexCount == 0) return;

        _shadowVB.SetData(_shadowVerts, 0, vertexCount, SetDataOptions.Discard);
        _shadowTriCount = vertexCount / 4 * 2;
    }

    // 0,1,2 0,2,3 for every quad. Built once per capacity, the pattern
    // never changes
    private static short[] BuildQuadIndices(int quadCount)
    {
        var indices = new short[quadCount * 6];
        for (int q = 0; q < quadCount; q++)
        {
            int v = q * 4;
            int i = q * 6;
            indices[i]     = (short)v;
            indices[i + 1] = (short)(v + 1);
            indices[i + 2] = (short)(v + 2);
            indices[i + 3] = (short)v;
            indices[i + 4] = (short)(v + 2);
            indices[i + 5] = (short)(v + 3);
        }
        return indices;
    }

    private void DrawRadialLights(Matrix viewProj)
    {
        if (_lightSoftEffect is null) return;

        var lightEffect = _lightSoftEffect;

        _lpViewProj?.SetValue(viewProj);
        _lpShadowMap?.SetValue(_shadowMap.Target);
        _lpShadowMapTexel?.SetValue(new Vector2(
            _shadowMap.Target is null ? 1f : 1f / _shadowMap.Target.Width,
            _shadowMap.Target is null ? 1f : 1f / _shadowMap.Target.Height));

        GameClient.GraphicsDevice.BlendState = AdditivePremultiplied;
        GameClient.GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GameClient.GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
        GameClient.GraphicsDevice.SamplerStates[1] = SamplerState.LinearClamp;
        GameClient.GraphicsDevice.RasterizerState = ScissorRasterizer;

        int vpW = _lightmap.Target!.Width;
        int vpH = _lightmap.Target!.Height;

        int shadowIdx = 0;
        foreach (var entry in _lights)
        {
            float radius = entry.Comp.Radius;
            BuildDiskQuad(entry.WorldPos, radius);

            float lidx = (entry.CastShadows && shadowIdx < _lighting.MaxShadowcastingLights)
                ? (shadowIdx + 0.5f) / _lighting.MaxShadowcastingLights
                : -1f;

            var techniqueName = entry.IsSpot
                ? (_lighting.HardShadows ? "SpotLightHard" : "SpotLightSoft")
                : (_lighting.HardShadows ? "LightHard" : "LightSoft");
            if (lightEffect.CurrentTechnique.Name != techniqueName)
                lightEffect.CurrentTechnique = lightEffect.Techniques[techniqueName];

            _lpCenter?.SetValue(entry.WorldPos);
            _lpColor?.SetValue(entry.Comp.Color.ToVector4());
            _lpRange?.SetValue(radius);
            _lpPower?.SetValue(entry.Comp.Intensity * _lighting.LightIntensity);
            _lpSoftness?.SetValue(entry.Comp.Softness * _lighting.LightSoftness);
            _lpFalloff?.SetValue(FalloffScalar(entry.Comp.Falloff));
            _lpCurveFactor?.SetValue(CurveFactorFor(entry.Comp.Falloff));
            _lpIndex?.SetValue(lidx);
            _lpContactBias?.SetValue(_lighting.ShadowContactBias / MathF.Max(radius, 0.0001f));

            if (entry.IsSpot)
            {
                _lpDirection?.SetValue(entry.Direction);
                _lpConeAngle?.SetValue(entry.ConeHalfAngle);
                _lpConeSoftness?.SetValue(entry.ConeSoftness);
            }

            GameClient.GraphicsDevice.ScissorRectangle = LightToScissor(entry.WorldPos, radius, viewProj, vpW, vpH);

            foreach (var pass in lightEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GameClient.GraphicsDevice.DrawUserPrimitives(
                    PrimitiveType.TriangleList, _diskQuad, 0, 2, DiskVertex.Declaration);
            }

            if (entry.CastShadows && shadowIdx < _lighting.MaxShadowcastingLights)
                shadowIdx++;
        }

        GameClient.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GameClient.GraphicsDevice.BlendState = BlendState.AlphaBlend;
    }

    private void DrawTextureLights()
    {
        var texLightSampler = _lighting.PixelatedLighting ? SamplerState.PointClamp : SamplerState.LinearClamp;
        GameClient.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Additive,
            texLightSampler,
            transformMatrix: _camera.GetViewMatrix());

        foreach (var (_, tex, transform) in GetEntitiesWithComp<TextureLightComponent, TransformComponent>())
        {
            if (string.IsNullOrEmpty(tex.Texture)) continue;
            if (!_assets.GetTexture(tex.Texture, out var spr, out var page)) continue;

            var worldPos = transform.Position + tex.Offset;

            // 1.5 covers any rotation of the sprite rect
            float maxDim = MathF.Max(
                spr.Region.Width * MathF.Abs(tex.Scale.X),
                spr.Region.Height * MathF.Abs(tex.Scale.Y)) * 1.5f;
            if (!_camera.IsOnScreen(worldPos, new Vector2(maxDim)))
                continue;

            var rotation = (tex.RotatesWithTransform ? transform.Angle : 0f) + tex.Rotation;
            var color = tex.Color * tex.Intensity * _lighting.LightIntensity;

            GameClient.SpriteBatch.Draw(
                page.Texture,
                worldPos,
                spr.Region,
                color,
                rotation,
                new Vector2(spr.Region.Width / 2f, spr.Region.Height / 2f),
                tex.Scale,
                SpriteEffects.None,
                0f);
        }

        GameClient.SpriteBatch.End();
    }

    // blurs the lightmap at half res, then draws the occluder quads over
    // the lightmap replacing each wall pixel with the blurred value, so
    // walls show the glow of nearby lights (Robust's wall bleed)
    private void RunWallBleed(Matrix viewProj)
    {
        if (_wallMergeEffect is null || _wallBleed.A is null || _wallBleed.B is null || _lightmap.Target is null)
            return;

        int needed = _occluders.Count * 6;
        if (_wallVerts.Length < needed)
            Array.Resize(ref _wallVerts, needed);

        int n = 0;
        foreach (var (b, _) in _occluders)
        {
            var tl = new Vector2(b.Left, b.Top);
            var tr = new Vector2(b.Right, b.Top);
            var br = new Vector2(b.Right, b.Bottom);
            var bl = new Vector2(b.Left, b.Bottom);
            _wallVerts[n++] = new DiskVertex { WorldPos = tl };
            _wallVerts[n++] = new DiskVertex { WorldPos = tr };
            _wallVerts[n++] = new DiskVertex { WorldPos = br };
            _wallVerts[n++] = new DiskVertex { WorldPos = tl };
            _wallVerts[n++] = new DiskVertex { WorldPos = br };
            _wallVerts[n++] = new DiskVertex { WorldPos = bl };
        }
        if (n == 0) return;

        // two blur iterations so the glow reaches deep enough into the walls
        BlurPass(_lightmap.Target, _wallBleed.A, 1f);
        BlurPass(_wallBleed.A, _wallBleed.B, 0f);
        BlurPass(_wallBleed.B, _wallBleed.A, 1f);
        BlurPass(_wallBleed.A, _wallBleed.B, 0f);

        _wmBlurred?.SetValue(_wallBleed.B);
        _wmViewProj?.SetValue(viewProj);

        GameClient.GraphicsDevice.SetRenderTarget(_lightmap.Target);
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, _lightmap.Target.Width, _lightmap.Target.Height);
        GameClient.GraphicsDevice.BlendState = BlendState.Opaque;
        GameClient.GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GameClient.GraphicsDevice.RasterizerState = RasterizerState.CullNone;

        foreach (var pass in _wallMergeEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GameClient.GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.TriangleList, _wallVerts, 0, n / 3, DiskVertex.Declaration);
        }

        GameClient.GraphicsDevice.BlendState = BlendState.AlphaBlend;
    }

    private void RunLightBlur()
    {
        if (_lightBlurEffect is null || _blurScratch.Target is null || _lightmap.Target is null)
            return;

        BlurPass(_lightmap.Target, _blurScratch.Target, 1f);
        BlurPass(_blurScratch.Target, _lightmap.Target, 0f);

        GameClient.GraphicsDevice.BlendState = BlendState.AlphaBlend;
    }

    private void BlurPass(Texture2D source, RenderTarget2D dest, float isHorizontal)
    {
        _blSourceMap?.SetValue(source);
        _blSourceTexel?.SetValue(new Vector2(1f / source.Width, 1f / source.Height));
        _blIsHorizontal?.SetValue(isHorizontal);

        GameClient.GraphicsDevice.SetRenderTarget(dest);
        GameClient.GraphicsDevice.BlendState = BlendState.Opaque;
        // SpriteBatch leaves CullCounterClockwise on, which would cull the quad
        GameClient.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GameClient.GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, dest.Width, dest.Height);

        foreach (var pass in _lightBlurEffect!.CurrentTechnique.Passes)
        {
            pass.Apply();
            GameClient.GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.TriangleList, ScreenQuad, 0, 2, ScreenVertex.Declaration);
        }
    }

    private void BuildDiskQuad(Vector2 center, float radius)
    {
        float r = radius;
        var tl = new Vector2(center.X - r, center.Y - r);
        var tr = new Vector2(center.X + r, center.Y - r);
        var br = new Vector2(center.X + r, center.Y + r);
        var bl = new Vector2(center.X - r, center.Y + r);
        _diskQuad[0] = new DiskVertex { WorldPos = tl };
        _diskQuad[1] = new DiskVertex { WorldPos = tr };
        _diskQuad[2] = new DiskVertex { WorldPos = br };
        _diskQuad[3] = new DiskVertex { WorldPos = tl };
        _diskQuad[4] = new DiskVertex { WorldPos = br };
        _diskQuad[5] = new DiskVertex { WorldPos = bl };
    }

    private static float FalloffScalar(FalloffMode mode) => mode switch
    {
        FalloffMode.Linear       => 1.0f,
        FalloffMode.Quadratic    => 2.0f,
        FalloffMode.InverseSquare => 4.0f,
        _ => 2.0f,
    };

    private static float CurveFactorFor(FalloffMode mode) => mode switch
    {
        FalloffMode.Linear       => 0f,
        FalloffMode.Quadratic    => 0.5f,
        FalloffMode.InverseSquare => 1f,
        _ => 0.5f,
    };

    // scissor rect (in lightmap pixels) that encloses the projected light quad
    private static Rectangle LightToScissor(Vector2 worldPos, float radius, Matrix viewProj, int vpW, int vpH)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        AccumProjectedCorner(ref minX, ref maxX, ref minY, ref maxY, worldPos.X - radius, worldPos.Y - radius, viewProj, vpW, vpH);
        AccumProjectedCorner(ref minX, ref maxX, ref minY, ref maxY, worldPos.X + radius, worldPos.Y - radius, viewProj, vpW, vpH);
        AccumProjectedCorner(ref minX, ref maxX, ref minY, ref maxY, worldPos.X + radius, worldPos.Y + radius, viewProj, vpW, vpH);
        AccumProjectedCorner(ref minX, ref maxX, ref minY, ref maxY, worldPos.X - radius, worldPos.Y + radius, viewProj, vpW, vpH);

        int x  = (int)MathF.Floor(MathHelper.Clamp(minX, 0f, vpW));
        int y  = (int)MathF.Floor(MathHelper.Clamp(minY, 0f, vpH));
        int x2 = (int)MathF.Ceiling(MathHelper.Clamp(maxX, 0f, vpW));
        int y2 = (int)MathF.Ceiling(MathHelper.Clamp(maxY, 0f, vpH));
        return new Rectangle(x, y, Math.Max(1, x2 - x), Math.Max(1, y2 - y));
    }

    private static void AccumProjectedCorner(
        ref float minX, ref float maxX, ref float minY, ref float maxY,
        float wx, float wy, Matrix viewProj, int vpW, int vpH)
    {
        var v = Vector4.Transform(new Vector4(wx, wy, 0f, 1f), viewProj);
        if (MathF.Abs(v.W) < 1e-6f) return;
        float ndcX = v.X / v.W;
        float ndcY = v.Y / v.W;
        // ndc y is +1 at the top, screen y is 0 at the top
        float sx = (ndcX + 1f) * 0.5f * vpW;
        float sy = (1f - ndcY) * 0.5f * vpH;
        if (sx < minX) minX = sx;
        if (sx > maxX) maxX = sx;
        if (sy < minY) minY = sy;
        if (sy > maxY) maxY = sy;
    }
}
