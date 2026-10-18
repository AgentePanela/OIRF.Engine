using System;
using System.Collections.Generic;
using Engine.Client.Assets;
using Engine.Shared.Common;
using Engine.Shared.Containers;
using Engine.Shared.GameObjects;
using Engine.Shared.Lighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Puts the lights of the world into the light map of every viewport, see <see cref="ILightRenderer"/>.
/// </summary>
public sealed partial class LightingSystem : EntitySystem, ILightRenderer
{
    [Dependency] private readonly IRenderManager _renderMan = default!;
    [Dependency] private readonly LightingManager _lighting = default!;
    [Dependency] private readonly IAssetManager _assets = default!;
    [Dependency] private readonly OccluderSystem _occluders = default!;

    // by texture key, null for a key that does not exist so it is only reported once
    private readonly Dictionary<string, (Texture2D Texture, Rectangle Region)?> _masks = new();

    public override void Init()
    {
        base.Init();
        ((RenderManager)_renderMan).LightRenderer = this;
    }

    public override void OnShutdown()
    {
        base.OnShutdown();

        var renderer = (RenderManager)_renderMan;
        if (renderer.LightRenderer == this)
            renderer.LightRenderer = null;

        DisposeRenderer();
    }

    Color ILightRenderer.GetAmbientColor()
    {
        // the one with the highest priority wins
        AmbientLightComponent? best = null;
        foreach (var (_, ambient) in GetEntitiesWithComp<AmbientLightComponent>())
        {
            if (best is null || ambient.Priority > best.Priority)
                best = ambient;
        }

        return best is null ? _lighting.AmbientLight : best.Color * best.Intensity;
    }

    void ILightRenderer.CollectLights(RectangleF bounds, float softnessScale, List<LightDraw> lights)
    {
        foreach (var (uid, light, transform) in GetEntitiesWithComp<PointLightComponent, TransformComponent>())
        {
            if (!light.Enabled || light.Radius <= 0f || light.Intensity <= 0f || HasComp<ContainedComponent>(uid))
                continue;

            var center = transform.Position + light.Offset;
            var radius = light.Radius;
            if (center.X + radius < bounds.Left || center.X - radius > bounds.Right
                || center.Y + radius < bounds.Top || center.Y - radius > bounds.Bottom)
                continue;

            Texture2D? mask = null;
            var region = Rectangle.Empty;
            if (!string.IsNullOrEmpty(light.Mask) && GetMask(light.Mask) is { } found)
                (mask, region) = found;

            var rotation = light.Rotation + (light.RotatesWithTransform ? transform.Angle : 0f);
            var halfCone = MathHelper.ToRadians(Math.Clamp(light.ConeAngle, 0f, 360f)) * 0.5f;

            lights.Add(new LightDraw
            {
                Owner = uid,
                Center = center,
                Radius = radius,
                Rotation = rotation,
                Color = light.Color,
                Brightness = light.Intensity * _lighting.LightIntensity,
                HalfCone = halfCone,
                ConeSoftness = Math.Clamp(light.ConeSoftness, 0f, 1f),
                Falloff = FalloffScalar(light.Falloff),
                Curve = CurveFactor(light.Falloff),
                Mask = mask,
                MaskRegion = region,
                CastShadows = light.CastShadows,
                SourceRadius = MathF.Max(light.Softness, 0f) * softnessScale,
            });
        }
    }

    void ILightRenderer.CollectShadowSegments(in LightDraw light, List<ShadowSegment> segments)
        => _occluders.CollectSegments(light.Center, light.Radius, segments);

    void ILightRenderer.TakeOccluderChanges(List<RectangleF> areas)
        => _occluders.TakeChanges(areas);

    private (Texture2D Texture, Rectangle Region)? GetMask(string key)
    {
        if (_masks.TryGetValue(key, out var mask))
            return mask;

        if (_assets.GetTexture(key, out var sprite, out var page))
            mask = (page.Texture, sprite.Region);
        else
            Log.Warn($"Unknown light mask texture '{key}'");

        _masks[key] = mask;
        return mask;
    }

    private static float FalloffScalar(FalloffMode mode) => mode switch
    {
        FalloffMode.Linear => 1f,
        FalloffMode.InverseSquare => 4f,
        _ => 2f,
    };

    private static float CurveFactor(FalloffMode mode) => mode switch
    {
        FalloffMode.Linear => 0f,
        FalloffMode.InverseSquare => 1f,
        _ => 0.5f,
    };
}
