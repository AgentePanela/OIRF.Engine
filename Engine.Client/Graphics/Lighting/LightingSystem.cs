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
/// Coordinates lighting components and produces the final lightmap each frame.
///
/// Pipeline (in order):
/// 1. Shadow pass — writes per-angle occluder distance into a 1D unwrapped
///    shadow map (one row per shadow-casting light).
/// 2. Occlusion mask — marks pixels reached by shadow-casting lights.
/// 3. Light pass — clears lightmap with ambient, then accumulates radial
///    light disks (point and spot) additively with soft PCF shadows.
/// 4. Wall bleed + light blur — separable Gaussian blur to soften banding.
/// 5. Apply pass — done in <see cref="ApplyAfterWorld"/> after the world is
///    drawn; multiplies the scene by the lightmap.
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
    private readonly OcclusionMaskRT _occlusionMask = new();

    // Occluder-edge geometry — rebuilt every frame.
    // 256 occluders × 4 edges × 4 verts = 4096 vertices, 6144 indices.
    private const int MaxOccluders = 256;
    private ShadowGeometry.OccluderVertex[] _shadowVerts = new ShadowGeometry.OccluderVertex[MaxOccluders * 16];
    private short[] _shadowIndices = new short[MaxOccluders * 24];

    // Reused scratch list for tile AABBs — cleared after CollectOccluders.
    private readonly List<Rectangle> _scratchTileRects = new();

    // Cached per-frame collections — avoids a new List<> allocation each frame.
    private readonly List<LightEntry> _lights = new();
    private readonly List<(Rectangle Bounds, TransformComponent Transform)> _occluders = new();
    // Per-light subset of _occluders — cleared and rebuilt inside RenderShadowMap for each shadow light.
    private readonly List<(Rectangle Bounds, TransformComponent Transform)> _culledOccluders = new();

    // World-space disk quad: two triangles covering a 2r×2r square around a light.
    private struct DiskVertex
    {
        public Vector2 WorldPos;
        public const int SizeInBytes = 8;
        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0));
    }
    private DiskVertex[] _diskQuad = new DiskVertex[6];

    // Clip-space fullscreen quad for post-process passes.
    private struct ScreenVertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
        public const int SizeInBytes = 16;
        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));
    }
    private ScreenVertex[] _screenQuad = new ScreenVertex[6];

    // One, One blend — premultiplied RGB light contribution, A its strength.
    private static readonly BlendState AdditivePremultiplied = new BlendState
    {
        ColorSourceBlend      = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend      = Blend.One,
        AlphaDestinationBlend = Blend.One,
        ColorBlendFunction    = BlendFunction.Add,
        AlphaBlendFunction    = BlendFunction.Add,
    };

    // Scissor test enabled; CullMode.None because disk quads may have
    // arbitrary winding after the camera transform flips the Y axis.
    private static readonly RasterizerState ScissorRasterizer = new RasterizerState
    {
        CullMode = CullMode.None,
        ScissorTestEnable = true,
    };

    // LESS (not LessEqual) so overlapping occluder slices resolve to the
    // closest one without corrupting the VSM mean-of-squares in the G channel.
    private static readonly DepthStencilState ShadowDepthState = new DepthStencilState
    {
        DepthBufferEnable       = true,
        DepthBufferWriteEnable  = true,
        DepthBufferFunction     = CompareFunction.Less,
    };

    private Effect? _applyEffect;
    private Effect? _debugEffect;
    private Effect? _shadowDepthEffect;
    private Effect? _lightSoftEffect;
    private Effect? _lightBlurEffect;
    private Effect? _wallMergeEffect;
    private Effect? _occlusionMaskEffect;

    private readonly Stopwatch _passTimer = new();
    private readonly Stopwatch _frameTimer = new();

    public override void Init()
    {
        base.Init();

        _cfg.Subs(LightingCvars.LightmapScale,    v => _lighting.LightmapScale    = v);
        _cfg.Subs(LightingCvars.PixelatedLighting, v => _lighting.PixelatedLighting = v);
        _cfg.Subs(LightingCvars.LightPixelSize,   v => _lighting.LightPixelSize   = v);

        _applyEffect         = _shaders.GetShader("LightingApply")?.Clone();
        _debugEffect         = _shaders.GetShader("LightingDebug")?.Clone();
        _shadowDepthEffect   = _shaders.GetShader("ShadowDepth")?.Clone();
        _lightSoftEffect     = _shaders.GetShader("LightSoft")?.Clone();
        _lightBlurEffect     = _shaders.GetShader("LightBlur")?.Clone();
        _wallMergeEffect     = _shaders.GetShader("WallMerge")?.Clone();
        _occlusionMaskEffect = _shaders.GetShader("OcclusionMask")?.Clone();

        if (_applyEffect is null)
            Log.Warn("LightingApply.fx not found - apply pass will be skipped.");
        if (_shadowDepthEffect is null)
            Log.Warn("ShadowDepth.fx not found - shadows will be disabled.");
        if (_lightSoftEffect is null)
            Log.Warn("LightSoft.fx not found - point/spot lights will not render.");
        if (_occlusionMaskEffect is null)
            Log.Warn("OcclusionMask.fx not found - wall bleed will be unrestricted.");
    }

    public override void Draw(float dt)
    {
        if (!_lighting.Enabled)
            return;

        BuildLightmap();
    }

    /// <summary>
    /// Multiply the lightmap over the rendered scene and blit to the backbuffer.
    /// Must be called after <see cref="RenderManager.DrawQueue"/> has written the
    /// world to <see cref="RenderManager.SceneTarget"/>.
    /// </summary>
    public void ApplyAfterWorld()
    {
        if (!_lighting.Enabled)
            return;

        var scene = _render.SceneTarget;
        if (scene is null || _lightmap.Target is null)
            return;

        GameClient.GraphicsDevice.SetRenderTarget(null);
        GameClient.GraphicsDevice.Viewport = _render.LastBackbufferViewport;

        if (_lighting.DebugDraw)
        {
            var vp = GameClient.GraphicsDevice.Viewport;
            GameClient.SpriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Opaque);
            GameClient.SpriteBatch.Draw(_lightmap.Target, new Rectangle(vp.X, vp.Y, vp.Width, vp.Height), Color.White);
            GameClient.SpriteBatch.End();
        }
        else if (_applyEffect is not null)
        {
            var techniqueName = _lighting.PixelatedLighting ? "PixelatedLight" : "SpriteDrawing";
            if (_applyEffect.CurrentTechnique.Name != techniqueName)
                _applyEffect.CurrentTechnique = _applyEffect.Techniques[techniqueName];

            _applyEffect.Parameters["Intensity"]?.SetValue(1.0f);
            _applyEffect.Parameters["AmbientColor"]?.SetValue(_lighting.AmbientLight.ToVector4());

            if (_lighting.PixelatedLighting && _lightmap.Target is not null)
            {
                var texelSize = new Vector2(
                    1f / _lightmap.Target.Width,
                    1f / _lightmap.Target.Height);
                _applyEffect.Parameters["LightmapTexelSize"]?.SetValue(texelSize);
            }

            _render.SubmitFullscreenEffectWithTextures(_applyEffect, scene, _lightmap.Target);
        }
    }

    public override void OnShutdown()
    {
        base.OnShutdown();
        _shadowMap.Dispose();
        _wallBleed.Dispose();
        _occlusionMask.Dispose();
    }

    // -----------------------------------------------------------------------
    //  Lightmap building
    // -----------------------------------------------------------------------

    private void BuildLightmap()
    {
        _frameTimer.Restart();
        double shadowPassMs = 0, occlusionMaskMs = 0, lightPassMs = 0, wallBleedMs = 0, lightBlurMs = 0;

        // Resolve ambient: highest-priority AmbientLightComponent wins.
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

        _wallBleed.EnsureSize(lightW, lightH);
        _occlusionMask.EnsureSize(lightW, lightH);

        CollectLights();
        CollectOccluders();
        int shadowLightCount = CountShadowLights();

        // viewProj shared between DrawPointLights and RenderOcclusionMask.
        var viewProj = _camera.GetViewMatrix() * Matrix.CreateOrthographicOffCenter(
            0, _viewport.VirtualWidth, _viewport.VirtualHeight, 0, -1, 1);

        if (_shadowDepthEffect is not null && _shadowMap.Target is not null)
        {
            _passTimer.Restart();
            RenderShadowMap();
            _passTimer.Stop();
            shadowPassMs = _passTimer.Elapsed.TotalMilliseconds;
        }

        if (_occlusionMaskEffect is not null && _occlusionMask.Usable)
        {
            _passTimer.Restart();
            RenderOcclusionMask(viewProj);
            _passTimer.Stop();
            occlusionMaskMs = _passTimer.Elapsed.TotalMilliseconds;
        }

        _passTimer.Restart();
        _render.BeginSceneRender(_lightmap.Target);
        GameClient.GraphicsDevice.Clear(baseAmbient);
        DrawRadialLights(viewProj);
        DrawTextureLights();
        _render.EndSceneRender();
        _passTimer.Stop();
        lightPassMs = _passTimer.Elapsed.TotalMilliseconds;

        if (_lighting.WallBleedEnabled && _wallBleed.A is not null && _wallBleed.B is not null)
        {
            _passTimer.Restart();
            RunWallBleed();
            _passTimer.Stop();
            wallBleedMs = _passTimer.Elapsed.TotalMilliseconds;
        }

        if (_lighting.LightBlurEnabled && _wallBleed.A is not null && _wallBleed.B is not null)
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
            shadowPassMs, occlusionMaskMs, lightPassMs, wallBleedMs, lightBlurMs);
    }

    // -----------------------------------------------------------------------
    //  Light / occluder collection
    // -----------------------------------------------------------------------

    private struct LightEntry
    {
        public EntityUid Uid;
        public IRadialLight Comp;
        public Vector2 WorldPos;
        public bool IsSpot;
        public float Direction;     // radians, valid only when IsSpot
        public float ConeHalfAngle; // radians, valid only when IsSpot
        public float ConeSoftness;  // radians, valid only when IsSpot
    }

    private void CollectLights()
    {
        _lights.Clear();

        foreach (var (uid, point, transform) in GetEntitiesWithComp<PointLightComponent, TransformComponent>())
        {
            var worldPos = transform.Position + point.Offset;
            if (!_camera.IsOnScreen(worldPos, new Vector2(point.Radius * 2f)))
                continue;
            _lights.Add(new LightEntry { Uid = uid, Comp = point, WorldPos = worldPos });
        }

        foreach (var (uid, spot, transform) in GetEntitiesWithComp<SpotLightComponent, TransformComponent>())
        {
            var worldPos = transform.Position + spot.Offset;
            if (!_camera.IsOnScreen(worldPos, new Vector2(spot.Radius * 2f)))
                continue;

            float direction = (spot.RotatesWithTransform ? transform.Angle : 0f) + spot.Direction;
            float halfAngle = MathHelper.ToRadians(spot.ConeAngle) * 0.5f;
            float softness = MathHelper.Clamp(halfAngle * 0.25f, 0.01f, halfAngle);

            _lights.Add(new LightEntry
            {
                Uid = uid,
                Comp = spot,
                WorldPos = worldPos,
                IsSpot = true,
                Direction = direction,
                ConeHalfAngle = halfAngle,
                ConeSoftness = softness,
            });
        }

        // Shadow casters last so closer non-shadow lights win the MaxLights cap.
        var cam = _camera.WorldCenter;
        _lights.Sort((a, b) =>
        {
            int sgn = a.Comp.CastShadows.CompareTo(b.Comp.CastShadows);
            if (sgn != 0) return sgn;
            return (a.WorldPos - cam).LengthSquared().CompareTo((b.WorldPos - cam).LengthSquared());
        });

        if (_lights.Count > _lighting.MaxLights)
            _lights.RemoveRange(_lighting.MaxLights, _lights.Count - _lighting.MaxLights);
    }

    private int CountShadowLights()
    {
        int count = 0;
        foreach (var entry in _lights)
        {
            if (!entry.Comp.CastShadows) continue;
            if (++count >= _lighting.MaxShadowcastingLights) return count;
        }
        return count;
    }

    private void CollectOccluders()
    {
        _occluders.Clear();

        foreach (var (uid, occluder, transform) in GetEntitiesWithComp<OccluderComponent, TransformComponent>())
        {
            var bounds = _occlusionSys.GetOccluderBounds(uid, occluder, transform, _entManager);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;
            _occluders.Add((bounds, transform));
        }

        var tilemapSys = _entManager.GetSystem<TilemapSystem>();
        if (tilemapSys is not null)
        {
            var cameraBounds = _camera.ViewportBounds;
            foreach (var (_, tilemap, tmTransform) in GetEntitiesWithComp<TilemapComponent, TransformComponent>())
            {
                tilemapSys.GetSolidTilesInArea(tilemap, tmTransform, cameraBounds, _scratchTileRects);
                foreach (var rect in _scratchTileRects)
                {
                    if (rect.Width <= 0 || rect.Height <= 0) continue;
                    _occluders.Add((rect, tmTransform));
                }
            }
            _scratchTileRects.Clear();
        }
    }

    // -----------------------------------------------------------------------
    //  Shadow pass
    // -----------------------------------------------------------------------

    private void RenderShadowMap()
    {
        if (!_shadowMap.Usable) return;

        var shadowMap = _shadowMap.Target!;
        var depthEffect = _shadowDepthEffect!;

        // Clear to "infinity" (1,1,0,1) so un-written texels read as no occluder.
        // Depth cleared to 1.0 (far) so the LESS test accepts the first write.
        GameClient.GraphicsDevice.SetRenderTarget(shadowMap);
        GameClient.GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer,
            new Color(255, 255, 0, 255), 1f, 0);

        var prevBlend = GameClient.GraphicsDevice.BlendState;
        var prevDepth = GameClient.GraphicsDevice.DepthStencilState;
        GameClient.GraphicsDevice.BlendState = BlendState.Opaque;
        GameClient.GraphicsDevice.DepthStencilState = ShadowDepthState;

        int shadowIdx = 0;
        foreach (var entry in _lights)
        {
            if (!entry.Comp.CastShadows) continue;
            if (shadowIdx >= _lighting.MaxShadowcastingLights)
            {
                Log.Warn(
                    $"LightingSystem: shadow cap ({_lighting.MaxShadowcastingLights}) reached. " +
                    $"Light uid={entry.Uid.Id} at ({entry.WorldPos.X:0},{entry.WorldPos.Y:0}) " +
                    "renders without shadows. Raise MaxShadowcastingLights or cull more lights.");
                continue;
            }

            // Per-light occluder culling: skip occluders whose AABB doesn't
            // overlap the light's circle, then build geometry only from what's left.
            CullOccludersForLight(entry.WorldPos, entry.Comp.Radius);
            int indexCount = ShadowGeometry.Build(_culledOccluders, _shadowVerts, _shadowIndices, out int vertexCount);

            GameClient.GraphicsDevice.Viewport = new Viewport(0, shadowIdx, shadowMap.Width, 1);
            depthEffect.Parameters["lightPos"]?.SetValue(entry.WorldPos);
            depthEffect.Parameters["lightRadius"]?.SetValue(entry.Comp.Radius);

            if (indexCount > 0)
            {
                int triangles = indexCount / 3;
                // Two draws: pass 0 = primary angular range, pass 1 = wrapped tail
                // around the ±π seam.
                for (int wrapPass = 0; wrapPass < 2; wrapPass++)
                {
                    depthEffect.Parameters["shadowWrapPass"]?.SetValue((float)wrapPass);
                    foreach (var pass in depthEffect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        GameClient.GraphicsDevice.DrawUserIndexedPrimitives(
                            PrimitiveType.TriangleList,
                            _shadowVerts, 0, vertexCount,
                            _shadowIndices, 0, triangles,
                            ShadowGeometry.OccluderVertex.Declaration);
                    }
                }
            }

            shadowIdx++;
        }

        GameClient.GraphicsDevice.BlendState = prevBlend;
        GameClient.GraphicsDevice.DepthStencilState = prevDepth;
    }

    // -----------------------------------------------------------------------
    //  Light pass
    // -----------------------------------------------------------------------

    private void DrawRadialLights(Matrix viewProj)
    {
        if (_lightSoftEffect is null) return;

        var lightEffect = _lightSoftEffect;

        lightEffect.Parameters["viewProj"]?.SetValue(viewProj);
        lightEffect.Parameters["ShadowMap"]?.SetValue(_shadowMap.Target);
        lightEffect.Parameters["shadowMapTexel"]?.SetValue(new Vector2(
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

            float lidx = (entry.Comp.CastShadows && shadowIdx < _lighting.MaxShadowcastingLights)
                ? (shadowIdx + 0.5f) / _lighting.MaxShadowcastingLights
                : -1f;

            var techniqueName = entry.IsSpot
                ? (_lighting.HardShadows ? "SpotLightHard" : "SpotLightSoft")
                : (_lighting.HardShadows ? "LightHard" : "LightSoft");
            if (lightEffect.CurrentTechnique.Name != techniqueName)
                lightEffect.CurrentTechnique = lightEffect.Techniques[techniqueName];

            lightEffect.Parameters["lightCenter"]?.SetValue(entry.WorldPos);
            lightEffect.Parameters["lightColor"]?.SetValue(entry.Comp.Color.ToVector4());
            lightEffect.Parameters["lightRange"]?.SetValue(radius);
            lightEffect.Parameters["lightRadius"]?.SetValue(radius);
            lightEffect.Parameters["lightPower"]?.SetValue(entry.Comp.Intensity * _lighting.LightIntensity);
            lightEffect.Parameters["lightSoftness"]?.SetValue(entry.Comp.Softness * _lighting.LightSoftness);
            lightEffect.Parameters["lightFalloff"]?.SetValue(FalloffScalar(entry.Comp.Falloff));
            lightEffect.Parameters["lightCurveFactor"]?.SetValue(CurveFactorFor(entry.Comp.Falloff));
            lightEffect.Parameters["lightIndex"]?.SetValue(lidx);
            lightEffect.Parameters["shadowContactBias"]?.SetValue(_lighting.ShadowContactBias / MathF.Max(radius, 0.0001f));

            if (entry.IsSpot)
            {
                lightEffect.Parameters["lightDirection"]?.SetValue(entry.Direction);
                lightEffect.Parameters["lightConeAngle"]?.SetValue(entry.ConeHalfAngle);
                lightEffect.Parameters["lightConeSoftness"]?.SetValue(entry.ConeSoftness);
            }

            GameClient.GraphicsDevice.ScissorRectangle = LightToScissor(entry.WorldPos, radius, viewProj, vpW, vpH);

            foreach (var pass in lightEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GameClient.GraphicsDevice.DrawUserPrimitives(
                    PrimitiveType.TriangleList, _diskQuad, 0, 2, DiskVertex.Declaration);
            }

            if (entry.Comp.CastShadows && shadowIdx < _lighting.MaxShadowcastingLights)
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

    // -----------------------------------------------------------------------
    //  Occlusion mask pass
    // -----------------------------------------------------------------------

    private void RenderOcclusionMask(Matrix viewProj)
    {
        if (_occlusionMaskEffect is null || _occlusionMask.Target is null) return;

        _occlusionMaskEffect.Parameters["viewProj"]?.SetValue(viewProj);
        _occlusionMaskEffect.Parameters["ShadowMap"]?.SetValue(_shadowMap.Target);
        _occlusionMaskEffect.Parameters["shadowMapTexel"]?.SetValue(new Vector2(
            _shadowMap.Target is null ? 1f : 1f / _shadowMap.Target.Width,
            _shadowMap.Target is null ? 1f : 1f / _shadowMap.Target.Height));

        var prevBlend  = GameClient.GraphicsDevice.BlendState;
        var prevDepth  = GameClient.GraphicsDevice.DepthStencilState;
        var prevSamp0  = GameClient.GraphicsDevice.SamplerStates[0];
        var prevSamp1  = GameClient.GraphicsDevice.SamplerStates[1];
        var prevRaster = GameClient.GraphicsDevice.RasterizerState;

        int vpW = _occlusionMask.Target.Width;
        int vpH = _occlusionMask.Target.Height;

        GameClient.GraphicsDevice.SetRenderTarget(_occlusionMask.Target);
        GameClient.GraphicsDevice.Clear(Color.Transparent);
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, vpW, vpH);
        GameClient.GraphicsDevice.BlendState = BlendState.Additive;
        GameClient.GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GameClient.GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
        GameClient.GraphicsDevice.SamplerStates[1] = SamplerState.LinearClamp;
        GameClient.GraphicsDevice.RasterizerState = ScissorRasterizer;

        int shadowIdx = 0;
        foreach (var entry in _lights)
        {
            if (!entry.Comp.CastShadows) continue;
            if (shadowIdx >= _lighting.MaxShadowcastingLights) continue;

            float radius = entry.Comp.Radius;
            BuildDiskQuad(entry.WorldPos, radius);

            var techniqueName = entry.IsSpot
                ? (_lighting.HardShadows ? "SpotOcclusionMaskHard" : "SpotOcclusionMask")
                : (_lighting.HardShadows ? "OcclusionMaskHard" : "OcclusionMask");
            if (_occlusionMaskEffect.CurrentTechnique.Name != techniqueName)
                _occlusionMaskEffect.CurrentTechnique = _occlusionMaskEffect.Techniques[techniqueName];

            _occlusionMaskEffect.Parameters["lightCenter"]?.SetValue(entry.WorldPos);
            _occlusionMaskEffect.Parameters["lightRange"]?.SetValue(radius);
            _occlusionMaskEffect.Parameters["lightRadius"]?.SetValue(radius);
            _occlusionMaskEffect.Parameters["lightSoftness"]?.SetValue(entry.Comp.Softness * _lighting.LightSoftness);
            _occlusionMaskEffect.Parameters["lightIndex"]?.SetValue((shadowIdx + 0.5f) / _lighting.MaxShadowcastingLights);
            _occlusionMaskEffect.Parameters["shadowContactBias"]?.SetValue(_lighting.ShadowContactBias / MathF.Max(radius, 0.0001f));

            if (entry.IsSpot)
            {
                _occlusionMaskEffect.Parameters["lightDirection"]?.SetValue(entry.Direction);
                _occlusionMaskEffect.Parameters["lightConeAngle"]?.SetValue(entry.ConeHalfAngle);
                _occlusionMaskEffect.Parameters["lightConeSoftness"]?.SetValue(entry.ConeSoftness);
            }

            GameClient.GraphicsDevice.ScissorRectangle = LightToScissor(entry.WorldPos, radius, viewProj, vpW, vpH);

            foreach (var pass in _occlusionMaskEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GameClient.GraphicsDevice.DrawUserPrimitives(
                    PrimitiveType.TriangleList, _diskQuad, 0, 2, DiskVertex.Declaration);
            }

            shadowIdx++;
        }

        GameClient.GraphicsDevice.BlendState = prevBlend;
        GameClient.GraphicsDevice.DepthStencilState = prevDepth;
        GameClient.GraphicsDevice.SamplerStates[0] = prevSamp0;
        GameClient.GraphicsDevice.SamplerStates[1] = prevSamp1;
        GameClient.GraphicsDevice.RasterizerState = prevRaster;
    }

    // -----------------------------------------------------------------------
    //  Wall bleed + light blur
    // -----------------------------------------------------------------------

    private void RunWallBleed()
    {
        if (_lightBlurEffect is null || _wallMergeEffect is null ||
            _wallBleed.A is null || _wallBleed.B is null || _lightmap.Target is null)
            return;

        // 2-pass separable Gaussian blur of the lightmap into wallBleed.B.
        BlurPass(_lightmap.Target, _wallBleed.A!, 1f);
        BlurPass(_wallBleed.A!, _wallBleed.B!, 0f);

        // Merge blurred lightmap back additively, restricted to lit pixels via
        // the occlusion mask — prevents the glow from bleeding into dark regions.
        _wallMergeEffect.Parameters["BlurredLightMap"]?.SetValue(_wallBleed.B);
        if (_occlusionMask.Usable)
            _wallMergeEffect.Parameters["OcclusionMask"]?.SetValue(_occlusionMask.Target);

        GameClient.GraphicsDevice.SetRenderTarget(_lightmap.Target);
        GameClient.GraphicsDevice.BlendState = BlendState.Additive;
        BuildScreenQuad(_screenQuad, _lightmap.Target.Width, _lightmap.Target.Height);

        foreach (var pass in _wallMergeEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GameClient.GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.TriangleList, _screenQuad, 0, 2, ScreenVertex.Declaration);
        }

        GameClient.GraphicsDevice.BlendState = BlendState.AlphaBlend;
    }

    private void RunLightBlur()
    {
        if (_lightBlurEffect is null || _wallBleed.A is null || _wallBleed.B is null || _lightmap.Target is null)
            return;

        // 3-pass separable Gaussian: H → V → H, end on lightmap.
        BlurPass(_lightmap.Target, _wallBleed.A!, 1f);
        BlurPass(_wallBleed.A!, _wallBleed.B!, 0f);
        BlurPass(_wallBleed.B!, _lightmap.Target, 1f);

        GameClient.GraphicsDevice.BlendState = BlendState.AlphaBlend;
    }

    private void BlurPass(Texture2D source, RenderTarget2D dest, float isHorizontal)
    {
        if (_lightBlurEffect is null) return;

        _lightBlurEffect.Parameters["SourceMap"]?.SetValue(source);
        _lightBlurEffect.Parameters["SourceTexel"]?.SetValue(new Vector2(1f / source.Width, 1f / source.Height));
        _lightBlurEffect.Parameters["isHorizontal"]?.SetValue(isHorizontal);
        _lightBlurEffect.Parameters["blurStrength"]?.SetValue(1.0f);

        GameClient.GraphicsDevice.SetRenderTarget(dest);
        GameClient.GraphicsDevice.BlendState = BlendState.Opaque;
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, dest.Width, dest.Height);
        BuildScreenQuad(_screenQuad, dest.Width, dest.Height);

        foreach (var pass in _lightBlurEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GameClient.GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.TriangleList, _screenQuad, 0, 2, ScreenVertex.Declaration);
        }
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

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

    private static void BuildScreenQuad(ScreenVertex[] quad, int w, int h)
    {
        quad[0] = new ScreenVertex { Position = new Vector2(-1, -1), TexCoord = new Vector2(0, 0) };
        quad[1] = new ScreenVertex { Position = new Vector2( 1, -1), TexCoord = new Vector2(1, 0) };
        quad[2] = new ScreenVertex { Position = new Vector2( 1,  1), TexCoord = new Vector2(1, 1) };
        quad[3] = new ScreenVertex { Position = new Vector2(-1, -1), TexCoord = new Vector2(0, 0) };
        quad[4] = new ScreenVertex { Position = new Vector2( 1,  1), TexCoord = new Vector2(1, 1) };
        quad[5] = new ScreenVertex { Position = new Vector2(-1,  1), TexCoord = new Vector2(0, 1) };
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

    // Fills _culledOccluders with every entry in _occluders whose AABB
    // intersects the circle at lightPos with the given radius.
    private void CullOccludersForLight(Vector2 lightPos, float radius)
    {
        _culledOccluders.Clear();
        float r2 = radius * radius;
        foreach (var occ in _occluders)
        {
            var b = occ.Bounds;
            // Nearest point on the AABB to the light center.
            float cx = MathHelper.Clamp(lightPos.X, b.Left, b.Right);
            float cy = MathHelper.Clamp(lightPos.Y, b.Top, b.Bottom);
            float dx = lightPos.X - cx;
            float dy = lightPos.Y - cy;
            if (dx * dx + dy * dy <= r2)
                _culledOccluders.Add(occ);
        }
    }

    // Returns the lightmap-space scissor rectangle that tightly encloses the
    // projected disk quad for a given light. Coordinates are clamped to
    // [0, vpW] × [0, vpH] so the rect is always valid for SetScissorRectangle.
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
        // NDC (+1 = top, −1 = bottom) → screen Y (0 = top, vpH = bottom).
        float sx = (ndcX + 1f) * 0.5f * vpW;
        float sy = (1f - ndcY) * 0.5f * vpH;
        if (sx < minX) minX = sx;
        if (sx > maxX) maxX = sx;
        if (sy < minY) minY = sy;
        if (sy > maxY) maxY = sy;
    }
}
