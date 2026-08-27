using System;
using System.Diagnostics;
using Engine.Client.Assets;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.Lighting;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Manages the light building of the game world.
/// </summary>
public sealed partial class LightingSystem : EntityDrawSystem
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

    // quad covering a 2r x 2r square around a light, in world space - shared
    // by DrawRadialLights' light quad and RunWallBleed's occluder quads
    private struct DiskVertex
    {
        public Vector2 WorldPos;
        public const int SizeInBytes = 8;
        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0));
    }

    private Effect? _shadowDepthEffect;
    private Effect? _lightSoftEffect;
    private Effect? _lightBlurEffect;
    private Effect? _wallMergeEffect;

    private readonly Stopwatch _passTimer = new();
    private readonly Stopwatch _frameTimer = new();

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
        SubscribeEvent<OccluderComponent, MoveEvent>(OnOccluderMoved);

        // a removed light's ShadowLightCache entry would otherwise linger
        // forever - harmless but wasteful for scenes with lots of short-lived
        // lights (projectiles, effects)
        SubscribeEvent<PointLightComponent, CompRemovedEvent>(OnPointLightRemoved);
        SubscribeEvent<SpotLightComponent, CompRemovedEvent>(OnSpotLightRemoved);

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
            GameClient.GraphicsDevice.SetRenderTarget(_render.FinalTarget);
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
            _lights.Count, _shadowRanges.Count, _occluders.Count,
            shadowW, shadowH,
            _frameTimer.Elapsed.TotalMilliseconds,
            shadowPassMs, _shadowBuildMs, _shadowSetupMs, _shadowDrawMs,
            lightPassMs, wallBleedMs, lightBlurMs);
    }
}
