using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Components.Lighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

public sealed partial class LightingSystem
{
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

    // reused every frame to avoid allocations
    private readonly List<LightEntry> _lights = new();

    private float _maxLightRadius;

    // parameter lookups by name are dictionary hits, cache them once
    private EffectParameter? _lpViewProj, _lpShadowMap, _lpShadowMapTexel,
        _lpCenter, _lpColor, _lpRange, _lpPower, _lpSoftness, _lpFalloff,
        _lpCurveFactor, _lpIndex, _lpContactBias,
        _lpDirection, _lpConeAngle, _lpConeSoftness;

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

    private DiskVertex[] _diskQuad = new DiskVertex[6];

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
