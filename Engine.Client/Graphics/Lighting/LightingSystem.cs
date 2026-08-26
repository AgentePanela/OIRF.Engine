using System.Collections.Generic;
using System.Diagnostics;
using System;
using Engine.Client.Assets;
using Engine.Client.Graphics.Shaders;
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
    [Dependency] private readonly RenderStats _stats = default!;
    [Dependency] private readonly TransformSystem _transformSys = default!;

    private readonly LightingRenderTarget _lightmap = new();
    private readonly ShadowMapRT _shadowMap = new();
    private readonly WallBleedRT _wallBleed = new();
    private readonly ScratchRT _blurScratch = new();

    // occluder edge geometry. Each shadow light gets its own range holding
    // only the occluders inside its radius, all packed into one buffer and
    // drawn with a per-light baseVertex. Capped at 4096 occluders per light
    // by the 16 bit index range
    private const int MaxOccluderCap = 4096;

    // ~6 MB of vertices. Beyond this the remaining lights render unshadowed
    private const int MaxShadowVertices = 1 << 18;

    private int _occluderCapacity = 256;
    private ShadowGeometry.OccluderVertex[] _shadowVerts = new ShadowGeometry.OccluderVertex[256 * 16];
    private DynamicVertexBuffer? _shadowVB;
    private int _shadowVBCapacity;
    private IndexBuffer? _shadowIB;

    // one entry per shadow-casting light, in the order RenderShadowMap and
    // DrawRadialLights walk _lights, so the shadow map row index lines up
    private struct ShadowRange
    {
        public Vector2 LightPos;
        public float LightRadius;
        public int VertexOffset;
        public int TriCount;
        public bool NeedsWrapPass;
    }
    private readonly List<ShadowRange> _shadowRanges = new();

    private readonly List<int> _scratchRectOccluders = new();

    // edge-adjacency buckets for entity-occluder culling, keyed by the shared
    // touching coordinate. Rebuilt every frame since _occluders is too
    private readonly Dictionary<int, List<(int Lo, int Hi, int Idx)>> _edgeTop = new();
    private readonly Dictionary<int, List<(int Lo, int Hi, int Idx)>> _edgeBottom = new();
    private readonly Dictionary<int, List<(int Lo, int Hi, int Idx)>> _edgeLeft = new();
    private readonly Dictionary<int, List<(int Lo, int Hi, int Idx)>> _edgeRight = new();

    // the bucket lists are handed back here instead of being dropped for the
    // GC - there's one per distinct edge coordinate, every frame
    private readonly Stack<List<(int Lo, int Hi, int Idx)>> _edgeListPool = new();

    // reused every frame to avoid allocations
    private readonly List<LightEntry> _lights = new();
    private readonly List<ShadowGeometry.ShadowOccluder> _occluders = new();
    private DiskVertex[] _wallVerts = new DiskVertex[256 * 6];

    private Rectangle _lastOccluderBounds;
    private bool _occludersDirty = true;

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
    private EffectParameter? _blSourceMap, _blSourceTexel, _blIsHorizontal, _blBlurScale;
    private EffectParameter? _wmBlurred, _wmViewProj, _wmBleedStrength;

    private readonly Stopwatch _passTimer = new();
    private readonly Stopwatch _frameTimer = new();
    private readonly Stopwatch _buildTimer = new();

    // shadow pass broken into its three parts, so it's possible to tell
    // geometry building from target setup from the per-light draws
    private double _shadowBuildMs;
    private double _shadowSetupMs;
    private double _shadowDrawMs;

    public override void Init()
    {
        base.Init();

        _cfg.Subs(LightingCvars.LightmapScale,    v => _lighting.LightmapScale    = v);
        _cfg.Subs(LightingCvars.PixelatedLighting, v => _lighting.PixelatedLighting = v);
        _cfg.Subs(LightingCvars.LightPixelSize,   v => _lighting.LightPixelSize   = v);

        // an occluder spawning/despawning changes what CollectOccluders should
        // find even if the camera hasn't moved, so force a rebuild next frame
        SubscribeEvent<OccluderComponent, CompAddedEvent>(OnOccluderAdded);
        SubscribeEvent<OccluderComponent, CompRemovedEvent>(OnOccluderRemoved);

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
            _blBlurScale    = p["blurScale"];
        }

        if (_wallMergeEffect is not null)
        {
            var p = _wallMergeEffect.Parameters;
            _wmBlurred      = p["BlurredLightMap"];
            _wmViewProj     = p["viewProj"];
            _wmBleedStrength = p["bleedStrength"];
        }
    }

    public override void Draw(float dt)
    {
        if (!_lighting.Enabled)
        {
            _lighting.ClearFrameStats();
            return;
        }

        BuildLightmap();

        // Exposed so shaded sprite shaders can sample the lightmap themselves
        // (RenderManager pushes this onto any shader declaring a LightMap
        // parameter) instead of relying on a full-screen post-process.
        _lighting.CurrentLightMap = _lightmap.Target;
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

        if (_lightmap.Target is null)
            return;

        if (_lighting.DebugDraw)
        {
            GameClient.GraphicsDevice.SetRenderTargetTracked(_render.FinalTarget, "FinalTarget");
            GameClient.GraphicsDevice.Viewport = _render.LastBackbufferViewport;

            // SpriteBatch coords are viewport relative, so draw at 0,0 -
            // the letterbox offset is already applied by the viewport
            _render.DrawFullscreenQuad(_lightmap.Target, BlendState.Opaque, SamplerState.PointClamp);
            return;
        }

        // No SceneTarget this frame means DrawQueue found no shape needing the
        // multiply and drew the world straight to the backbuffer, already lit
        // by the per-sprite shaders. Nothing left to composite.
        var scene = _render.SceneTarget;
        if (!_render.WorldOnSceneTarget || scene is null)
            return;

        // Multiply the lightmap onto SceneTarget in place. StencilTestShadedOnly
        // only lets the blend touch pixels stamped "shaded" (0) by DrawQueue, so
        // unshaded pixels (stencil 1) are left untouched at full brightness.
        // SceneTarget still holds its stencil contents from DrawQueue
        // (RenderTargetUsage.PreserveContents).
        GameClient.GraphicsDevice.SetRenderTargetTracked(scene, "SceneTarget");
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, scene.Width, scene.Height);
        _stats.AddFill("lighting.apply", (double)scene.Width * scene.Height);

        var lightSampler = _lighting.PixelatedLighting ? SamplerState.PointClamp : SamplerState.LinearClamp;
        _render.DrawFullscreenQuad(_lightmap.Target, RenderManager.LightMultiplyBlend, lightSampler, RenderManager.StencilTestShadedOnly);

        // Blit the now fully-lit SceneTarget onto FinalTarget/the backbuffer.
        GameClient.GraphicsDevice.SetRenderTargetTracked(_render.FinalTarget, "FinalTarget");
        GameClient.GraphicsDevice.Viewport = _render.LastBackbufferViewport;
        _stats.AddFill("lighting.blit", (double)_render.LastBackbufferViewport.Width * _render.LastBackbufferViewport.Height);
        _render.DrawFullscreenQuad(scene, BlendState.Opaque, SamplerState.PointClamp);
    }

    public override void OnShutdown()
    {
        base.OnShutdown();
        _shadowMap.Dispose();
        _wallBleed.Dispose();
        _blurScratch.Dispose();
        _shadowVB?.Dispose(); _shadowVB = null; _shadowVBCapacity = 0;
        _shadowIB?.Dispose(); _shadowIB = null;
        _shadowRanges.Clear();
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

        var scene = _render.SceneTarget;
        int sceneW = scene?.Width ?? _viewport.VirtualWidth;
        int sceneH = scene?.Height ?? _viewport.VirtualHeight;

        int lightW, lightH;
        if (_lighting.PixelatedLighting)
        {
            int ps = _lighting.LightPixelSize;
            lightW = Math.Max(1, (sceneW + ps - 1) / ps);
            lightH = Math.Max(1, (sceneH + ps - 1) / ps);
        }
        else
        {
            float scale = _lighting.LightmapScale;
            lightW = Math.Max(1, (int)(sceneW * scale));
            lightH = Math.Max(1, (int)(sceneH * scale));
        }
        _lightmap.EnsureSize(lightW, lightH);
        if (_lightmap.Target is null)
        {
            _lighting.ClearFrameStats();
            return;
        }

        CollectLights();
        CollectOccluders();

        // one shadow map row per shadow-casting light, rounded up to a power
        // of two and never shrunk, so lights entering and leaving the view
        // don't reallocate the target every frame
        int shadowW = _lighting.ShadowMapSize;
        int shadowH = Math.Max(_shadowMap.Height, RoundUpPow2(Math.Max(1, CountShadowLights())));
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
            // half res like the bleed blur - this one only smooths shadow
            // banding, which survives the downscale, and the vertical pass
            // upscales back into the lightmap for free extra smoothing
            _blurScratch.EnsureSize(Math.Max(1, lightW / 2), Math.Max(1, lightH / 2));
            lightBlur = _blurScratch.Target is not null;
        }
        else
        {
            _blurScratch.Dispose();
        }

        // shared by every pass that rasterizes world-space quads - bounds must match the
        // canvas rectangle SceneTarget captures (sceneW/sceneH above), not the fixed virtual
        // viewport size, or the light/shadow geometry ends up misaligned with the sprites
        // once the two diverge (editor FinalTarget).
        var viewProj = _camera.GetViewMatrix() * Matrix.CreateOrthographicOffCenter(
            0, sceneW, sceneH, 0, -1, 1);

        if (_shadowDepthEffect is not null && _shadowMap.Target is not null)
        {
            _passTimer.Restart();
            RenderShadowMap();
            _passTimer.Stop();
            shadowPassMs = _passTimer.Elapsed.TotalMilliseconds;
        }
        else
        {
            // no shadow pass this frame, so no light may index into the map
            _shadowRanges.Clear();
        }

        _passTimer.Restart();
        _render.BeginSceneRender(_lightmap.Target, "Lightmap");
        GameClient.GraphicsDevice.ClearTracked(baseAmbient, "Lightmap");
        _stats.AddFill("lighting.lightmap.clear", (double)lightW * lightH);
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
            _lights.Count, _shadowRanges.Count, _occluders.Count,
            shadowW, shadowH,
            _frameTimer.Elapsed.TotalMilliseconds,
            shadowPassMs, _shadowBuildMs, _shadowSetupMs, _shadowDrawMs,
            lightPassMs, wallBleedMs, lightBlurMs);
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
            // IsOnScreen takes a top-left corner, so the disk box has to be
            // offset back by the radius - passing the center culls lights that
            // are off the right/bottom edge but still reaching into the view
            if (!_camera.IsOnScreen(worldPos - new Vector2(point.Radius), new Vector2(point.Radius * 2f)))
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
            if (!_camera.IsOnScreen(worldPos - new Vector2(spot.Radius), new Vector2(spot.Radius * 2f)))
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

    private static int RoundUpPow2(int value)
    {
        int result = 1;
        while (result < value)
            result *= 2;
        return result;
    }

    private void CollectOccluders()
    {
        // an occluder within a light radius of the view can still push a
        // shadow into the view, so pad the culling bounds by the biggest one
        var bounds = _camera.ViewportBounds;
        int pad = (int)MathF.Ceiling(_maxLightRadius);
        bounds.Inflate(pad, pad);

        if (!_occludersDirty && bounds == _lastOccluderBounds && !AnyOccluderMoved())
            return;

        _occluders.Clear();
        _scratchRectOccluders.Clear();

        foreach (var (uid, occluder, transform) in GetEntitiesWithComp<OccluderComponent, TransformComponent>())
        {
            // this walks every occluder in the scene, so reject the far ones
            // off the component alone - resolving a sprite occluder's bounds
            // costs a component lookup and sometimes an atlas hit
            if (!_occlusionSys.MayOverlap(occluder, transform, bounds)) continue;

            var b = _occlusionSys.GetOccluderBounds(uid, occluder, transform, _entManager);
            if (b.Width <= 0 || b.Height <= 0) continue;
            if (!bounds.Intersects(b)) continue;

            _occluders.Add(new ShadowGeometry.ShadowOccluder { Bounds = b, Transform = transform });

            // only true rectangles participate in edge culling - a circle's
            // silhouette curves inward from its AABB corners, so culling it
            // like a rectangle could hide a real gap in the shadow
            if (occluder.Shape == OccluderShape.Rectangle)
                _scratchRectOccluders.Add(_occluders.Count - 1);
        }
        CullTouchingEntityEdges(_scratchRectOccluders);

        _lastOccluderBounds = bounds;
        _occludersDirty = false;
    }

    private bool AnyOccluderMoved()
    {
        foreach (var uid in _transformSys.MovedThisFrame)
        {
            if (HasComp<OccluderComponent>(uid))
                return true;
        }
        return false;
    }

    private void OnOccluderAdded(EntityUid uid, OccluderComponent comp, CompAddedEvent args)
        => _occludersDirty = true;

    private void OnOccluderRemoved(EntityUid uid, OccluderComponent comp, CompRemovedEvent args)
        => _occludersDirty = true;

    // an entity occluder's edge is an interior seam - and gets culled - when
    // another occluder's opposite-facing edge sits exactly on it and fully
    // spans it (e.g. two wall segments placed edge to edge)
    private void CullTouchingEntityEdges(List<int> indices)
    {
        if (indices.Count < 2) return;

        RecycleEdgeBuckets(_edgeTop);
        RecycleEdgeBuckets(_edgeBottom);
        RecycleEdgeBuckets(_edgeLeft);
        RecycleEdgeBuckets(_edgeRight);

        foreach (var i in indices)
        {
            var b = _occluders[i].Bounds;
            Bucket(_edgeTop, b.Top, b.Left, b.Right, i);
            Bucket(_edgeBottom, b.Bottom, b.Left, b.Right, i);
            Bucket(_edgeLeft, b.Left, b.Top, b.Bottom, i);
            Bucket(_edgeRight, b.Right, b.Top, b.Bottom, i);
        }

        foreach (var i in indices)
        {
            var occ = _occluders[i];
            var b = occ.Bounds;
            occ.BlockedTop    = Covered(_edgeBottom, b.Top,    b.Left, b.Right, i);
            occ.BlockedBottom = Covered(_edgeTop,    b.Bottom, b.Left, b.Right, i);
            occ.BlockedLeft   = Covered(_edgeRight,  b.Left,   b.Top,  b.Bottom, i);
            occ.BlockedRight  = Covered(_edgeLeft,   b.Right,  b.Top,  b.Bottom, i);
            _occluders[i] = occ;
        }
    }

    private void RecycleEdgeBuckets(Dictionary<int, List<(int Lo, int Hi, int Idx)>> map)
    {
        foreach (var list in map.Values)
        {
            list.Clear();
            _edgeListPool.Push(list);
        }
        map.Clear();
    }

    private void Bucket(Dictionary<int, List<(int Lo, int Hi, int Idx)>> map, int key, int lo, int hi, int idx)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = _edgeListPool.Count > 0 ? _edgeListPool.Pop() : new List<(int, int, int)>();
        list.Add((lo, hi, idx));
    }

    private static bool Covered(Dictionary<int, List<(int Lo, int Hi, int Idx)>> opposite, int key, int lo, int hi, int selfIdx)
    {
        if (!opposite.TryGetValue(key, out var list)) return false;
        foreach (var (nlo, nhi, idx) in list)
        {
            if (idx != selfIdx && nlo <= lo && nhi >= hi) return true;
        }
        return false;
    }

    private void RenderShadowMap()
    {
        _shadowBuildMs = _shadowSetupMs = _shadowDrawMs = 0;

        if (!_shadowMap.Usable)
        {
            _shadowRanges.Clear();
            return;
        }

        var shadowMap = _shadowMap.Target!;
        var depthEffect = _shadowDepthEffect!;

        _buildTimer.Restart();
        BuildShadowGeometry();
        _buildTimer.Stop();
        _shadowBuildMs = _buildTimer.Elapsed.TotalMilliseconds;

        // clear color = "no occluder", depth = far so the LESS test accepts the first write
        _buildTimer.Restart();
        GameClient.GraphicsDevice.SetRenderTargetTracked(shadowMap, "ShadowMap");
        GameClient.GraphicsDevice.ClearTracked(ClearOptions.Target | ClearOptions.DepthBuffer,
            new Color(255, 255, 0, 255), 1f, 0, "ShadowMap");
        _stats.AddFill("lighting.shadowmap", (double)shadowMap.Width * shadowMap.Height);
        _buildTimer.Stop();
        _shadowSetupMs = _buildTimer.Elapsed.TotalMilliseconds;

        if (_shadowRanges.Count == 0 || _shadowVB is null || _shadowIB is null)
            return;

        _buildTimer.Restart();

        var prevBlend = GameClient.GraphicsDevice.BlendState;
        var prevDepth = GameClient.GraphicsDevice.DepthStencilState;
        GameClient.GraphicsDevice.BlendState = BlendState.Opaque;
        GameClient.GraphicsDevice.DepthStencilState = ShadowDepthState;
        GameClient.GraphicsDevice.SetVertexBuffer(_shadowVB);
        GameClient.GraphicsDevice.Indices = _shadowIB;

        for (int shadowIdx = 0; shadowIdx < _shadowRanges.Count; shadowIdx++)
        {
            var range = _shadowRanges[shadowIdx];
            if (range.TriCount == 0)
                continue;

            GameClient.GraphicsDevice.Viewport = new Viewport(0, shadowIdx, shadowMap.Width, 1);
            _sdLightPos?.SetValue(range.LightPos);
            _sdLightRadius?.SetValue(range.LightRadius);

            // pass 0 = normal range, pass 1 = tail that wraps around the +-pi
            // seam. Skipped entirely when no edge of this light's geometry
            // straddles the seam, which is the usual case
            int passes = range.NeedsWrapPass ? 2 : 1;
            for (int wrapPass = 0; wrapPass < passes; wrapPass++)
            {
                _sdWrapPass?.SetValue((float)wrapPass);
                foreach (var pass in depthEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    // baseVertex offsets into this light's slice, so the index
                    // buffer stays a single 0-based quad pattern
                    GameClient.GraphicsDevice.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList, range.VertexOffset, 0, range.TriCount);
                }
            }
        }

        GameClient.GraphicsDevice.SetVertexBuffer(null);
        GameClient.GraphicsDevice.Indices = null;
        GameClient.GraphicsDevice.BlendState = prevBlend;
        GameClient.GraphicsDevice.DepthStencilState = prevDepth;

        _buildTimer.Stop();
        _shadowDrawMs = _buildTimer.Elapsed.TotalMilliseconds;
    }

    // builds one geometry range per shadow-casting light, holding only the
    // occluders that light actually reaches, and uploads them all in one go
    private void BuildShadowGeometry()
    {
        _shadowRanges.Clear();

        int count = Math.Min(_occluders.Count, MaxOccluderCap);
        if (count == 0) return;

        EnsureOccluderCapacity(count);

        int vIdx = 0;

        // the map is rounded up to a power of two and never shrinks, so the
        // budget is the cvar, not however many rows happen to be allocated
        int rows = Math.Min(_shadowMap.Height, _lighting.MaxShadowcastingLights);

        foreach (var entry in _lights)
        {
            if (!entry.CastShadows) continue;
            if (_shadowRanges.Count >= rows)
            {
                if (!_warnedShadowCap)
                {
                    Log.Warn(
                        $"LightingSystem: shadow cap ({rows}) reached, extra lights render " +
                        "without shadows. Raise MaxShadowcastingLights or cull more lights.");
                    _warnedShadowCap = true;
                }
                break;
            }

            ShadowGeometry.BuildResult result;
            while (true)
            {
                result = ShadowGeometry.Build(
                    _occluders, count, _shadowVerts, vIdx, entry.WorldPos, entry.Comp.Radius);

                // the array only fills up once per scene, then it's reused
                if (!result.Truncated || !GrowShadowVerts())
                    break;
            }

            _shadowRanges.Add(new ShadowRange
            {
                LightPos = entry.WorldPos,
                LightRadius = entry.Comp.Radius,
                VertexOffset = vIdx,
                TriCount = result.VertexCount / 4 * 2,
                NeedsWrapPass = result.NeedsWrapPass,
            });

            vIdx += result.VertexCount;
        }

        if (vIdx == 0) return;

        EnsureShadowVertexBuffer(vIdx);
        _shadowVB!.SetData(_shadowVerts, 0, vIdx, SetDataOptions.Discard);
    }

    // the index buffer is sized for the worst-case single light, since
    // baseVertex makes every range index from 0
    private void EnsureOccluderCapacity(int occluderCount)
    {
        if (occluderCount <= _occluderCapacity && _shadowIB is not null)
            return;

        while (_occluderCapacity < occluderCount)
            _occluderCapacity *= 2;
        _occluderCapacity = Math.Min(_occluderCapacity, MaxOccluderCap);

        _shadowIB?.Dispose();
        _shadowIB = new IndexBuffer(GameClient.GraphicsDevice,
            IndexElementSize.SixteenBits, _occluderCapacity * 24, BufferUsage.WriteOnly);
        _shadowIB.SetData(BuildQuadIndices(_occluderCapacity * 4));
    }

    private bool GrowShadowVerts()
    {
        if (_shadowVerts.Length >= MaxShadowVertices)
            return false;

        int size = Math.Min(_shadowVerts.Length * 2, MaxShadowVertices);
        Array.Resize(ref _shadowVerts, size);
        return true;
    }

    private void EnsureShadowVertexBuffer(int vertexCount)
    {
        if (_shadowVB is not null && _shadowVBCapacity >= vertexCount)
            return;

        _shadowVBCapacity = Math.Max(_shadowVBCapacity, 256 * 16);
        while (_shadowVBCapacity < vertexCount)
            _shadowVBCapacity *= 2;

        _shadowVB?.Dispose();
        _shadowVB = new DynamicVertexBuffer(GameClient.GraphicsDevice,
            ShadowGeometry.OccluderVertex.Declaration, _shadowVBCapacity, BufferUsage.WriteOnly);
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

        var previousScissor = GameClient.GraphicsDevice.ScissorRectangle;

        GameClient.GraphicsDevice.BlendState = AdditivePremultiplied;
        GameClient.GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GameClient.GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
        GameClient.GraphicsDevice.SamplerStates[1] = SamplerState.LinearClamp;
        GameClient.GraphicsDevice.RasterizerState = ScissorRasterizer;

        int vpW = _lightmap.Target!.Width;
        int vpH = _lightmap.Target!.Height;

        int shadowRows = _shadowMap.Height;

        int shadowIdx = 0;
        foreach (var entry in _lights)
        {
            float radius = entry.Comp.Radius;
            _stats.AddFill("lighting.radial", ScreenQuadPixels(radius));
            BuildDiskQuad(entry.WorldPos, radius);

            // -1 skips the shadow lookup in the shader. A light with an empty
            // range has no occluder in reach, so it's lit everywhere and pays
            // nothing for the PCF taps
            bool hasShadowRow = entry.CastShadows
                && shadowIdx < _shadowRanges.Count
                && _shadowRanges[shadowIdx].TriCount > 0;

            float lidx = hasShadowRow
                ? (shadowIdx + 0.5f) / shadowRows
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

            // walks _shadowRanges in lockstep with BuildShadowGeometry, which
            // adds a range per shadow caster in this same order
            if (entry.CastShadows && shadowIdx < _shadowRanges.Count)
                shadowIdx++;
        }

        GameClient.GraphicsDevice.ScissorRectangle = previousScissor;
        GameClient.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GameClient.GraphicsDevice.BlendState = BlendState.AlphaBlend;
    }

    private void DrawTextureLights()
    {
        // opened lazily - most scenes have no texture lights at all, and an
        // empty Begin/End still churns device state every frame
        bool batchOpen = false;

        foreach (var (_, tex, transform) in GetEntitiesWithComp<TextureLightComponent, TransformComponent>())
        {
            if (string.IsNullOrEmpty(tex.Texture)) continue;
            if (!_assets.GetTexture(tex.Texture, out var spr, out var page)) continue;

            var worldPos = transform.Position + tex.Offset;

            // 1.5 covers any rotation of the sprite rect
            float maxDim = MathF.Max(
                spr.Region.Width * MathF.Abs(tex.Scale.X),
                spr.Region.Height * MathF.Abs(tex.Scale.Y)) * 1.5f;
            // sprite is drawn centered on worldPos, so offset to the corner
            if (!_camera.IsOnScreen(worldPos - new Vector2(maxDim * 0.5f), new Vector2(maxDim)))
                continue;

            var rotation = (tex.RotatesWithTransform ? transform.Angle : 0f) + tex.Rotation;
            var color = tex.Color * tex.Intensity * _lighting.LightIntensity;

            if (!batchOpen)
            {
                var texLightSampler = _lighting.PixelatedLighting ? SamplerState.PointClamp : SamplerState.LinearClamp;
                GameClient.SpriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.Additive,
                    texLightSampler,
                    transformMatrix: _camera.GetViewMatrix());
                batchOpen = true;
            }

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

        if (batchOpen)
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
        foreach (var occ in _occluders)
        {
            var b = occ.Bounds;
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

        // Chaining n gaussians of sigma s gives sigma*sqrt(n), so scaling the
        // tap spacing by sqrt(2/n) keeps the total width fixed however many
        // iterations run - iterations buy kernel quality, not reach. Reach is
        // WallBleedRadius, which stretches the whole thing.
        int iterations = _lighting.WallBleedIterations;
        float blurScale = MathF.Sqrt(2f / iterations) * _lighting.WallBleedRadius;

        Texture2D source = _lightmap.Target;
        for (int i = 0; i < iterations; i++)
        {
            BlurPass(source, _wallBleed.A, 1f, blurScale, "WallBleedA");
            BlurPass(_wallBleed.A, _wallBleed.B, 0f, blurScale, "WallBleedB");
            source = _wallBleed.B;
        }

        _wmBlurred?.SetValue(_wallBleed.B);
        _wmViewProj?.SetValue(viewProj);
        _wmBleedStrength?.SetValue(_lighting.WallBleedStrength);

        GameClient.GraphicsDevice.SetRenderTargetTracked(_lightmap.Target, "Lightmap");
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, _lightmap.Target.Width, _lightmap.Target.Height);
        _stats.AddFill("lighting.wallbleed.merge", (double)_lightmap.Target.Width * _lightmap.Target.Height);
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

        BlurPass(_lightmap.Target, _blurScratch.Target, 1f, 1f, "BlurScratch");
        BlurPass(_blurScratch.Target, _lightmap.Target, 0f, 1f, "Lightmap");

        GameClient.GraphicsDevice.BlendState = BlendState.AlphaBlend;
    }

    private void BlurPass(Texture2D source, RenderTarget2D dest, float isHorizontal, float blurScale, string destName)
    {
        _blSourceMap?.SetValue(source);
        _blSourceTexel?.SetValue(new Vector2(1f / source.Width, 1f / source.Height));
        _blIsHorizontal?.SetValue(isHorizontal);
        _blBlurScale?.SetValue(blurScale);

        GameClient.GraphicsDevice.SetRenderTargetTracked(dest, destName);
        _stats.AddFill($"lighting.blur.{destName}", (double)dest.Width * dest.Height);
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

    /// <summary>
    /// Lightmap pixels a light of this radius covers, clamped to the map - the
    /// fill estimate the profiler reports.
    /// </summary>
    private double ScreenQuadPixels(float radius)
    {
        if (_lightmap.Target is null)
            return 0;

        var side = radius * 2f * _camera.Zoom * _lighting.LightmapScale;
        var w = Math.Min(side, _lightmap.Target.Width);
        var h = Math.Min(side, _lightmap.Target.Height);
        return Math.Max(0, w * h);
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
