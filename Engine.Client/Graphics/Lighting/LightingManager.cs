using System;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Runtime configuration for the lighting pipeline. Resolve via IoC.
/// </summary>
public sealed class LightingManager
{
    /// <summary>
    /// Master toggle. When false the lighting system does no work at all.
    /// </summary>
    public bool Enabled { get; private set; } = false;

    /// <summary>
    /// Ambient color used when the scene has no AmbientLightComponent.
    /// </summary>
    public Color AmbientLight { get; set; } = new Color(0, 0, 0);

    /// <summary>
    /// Global multiplier applied on top of every light's intensity.
    /// </summary>
    public float LightIntensity { get; set; } = 1f;

    /// <summary>
    /// Max lights processed per frame, sorted by distance to the camera.
    /// </summary>
    public int MaxLights { get; set; } = 64;

    /// <summary>
    /// Shadow-casting light budget. Also the height of the shadow map (one
    /// row per light). Lights over the cap still render, just without shadows.
    /// </summary>
    public int MaxShadowcastingLights { get; set; } = 16;

    /// <summary>
    /// Shadow map width in texels. Each row is a full 360° unwrap around one
    /// light, so too few texels makes shadows look like ray slices.
    /// </summary>
    public int ShadowMapSize { get; set; } = 1024;

    /// <summary>
    /// Extra shadow bias in world pixels, so filtered light doesn't leave a
    /// bright fringe around wall edges.
    /// </summary>
    public float ShadowContactBias { get; set; } = 1.5f;

    /// <summary>
    /// Multiplier on the soft-shadow kernel size. Larger = wider penumbra.
    /// </summary>
    public float LightSoftness { get; set; } = 1.0f;

    /// <summary>
    /// Blurs light back over occluder edges to fake light scatter on walls.
    /// </summary>
    public bool WallBleedEnabled { get; set; } = false;

    /// <summary>
    /// Gaussian blur over the finished lightmap, smooths shadow banding.
    /// </summary>
    public bool LightBlurEnabled { get; set; } = false;

    /// <summary>
    /// Fraction of the viewport resolution used for the lightmap.
    /// 1.0 = native, 0.5 = half res (~4x cheaper fill, blurrier edges).
    /// </summary>
    public float LightmapScale
    {
        get => _lightmapScale;
        set => _lightmapScale = Math.Clamp(value, 0.1f, 1.0f);
    }
    private float _lightmapScale = 1.0f;

    /// <summary>
    /// Renders the lightmap at one texel per LightPixelSize screen pixels
    /// and upscales with nearest-neighbour, for a chunky pixel-art look.
    /// </summary>
    public bool PixelatedLighting { get; set; } = false;

    /// <summary>
    /// Size of each light "pixel" in screen pixels when PixelatedLighting is on.
    /// </summary>
    public int LightPixelSize
    {
        get => _lightPixelSize;
        set => _lightPixelSize = Math.Clamp(value, 1, 64);
    }
    private int _lightPixelSize = 8;

    /// <summary>
    /// Draws the raw lightmap instead of applying it. Debug only.
    /// </summary>
    public bool DebugDraw { get; set; } = false;

    /// <summary>
    /// Single-sample shadows instead of the 7-tap PCF kernel. Cheaper,
    /// hard edges.
    /// </summary>
    public bool HardShadows { get; set; } = false;

    public int LastVisibleLights { get; private set; }
    public int LastShadowLights { get; private set; }
    public int LastOccluders { get; private set; }
    public int LastShadowMapWidth { get; private set; }
    public int LastShadowMapHeight { get; private set; }
    public double LastLightingTotalMs { get; private set; }
    public double LastShadowPassMs { get; private set; }
    public double LastOcclusionMaskMs { get; private set; }
    public double LastLightPassMs { get; private set; }
    public double LastWallBleedMs { get; private set; }
    public double LastLightBlurMs { get; private set; }

    internal void RecordFrameStats(
        int visibleLights,
        int shadowLights,
        int occluders,
        int shadowMapWidth,
        int shadowMapHeight,
        double totalMs,
        double shadowPassMs,
        double occlusionMaskMs,
        double lightPassMs,
        double wallBleedMs,
        double lightBlurMs)
    {
        LastVisibleLights = visibleLights;
        LastShadowLights = shadowLights;
        LastOccluders = occluders;
        LastShadowMapWidth = shadowMapWidth;
        LastShadowMapHeight = shadowMapHeight;
        LastLightingTotalMs = totalMs;
        LastShadowPassMs = shadowPassMs;
        LastOcclusionMaskMs = occlusionMaskMs;
        LastLightPassMs = lightPassMs;
        LastWallBleedMs = wallBleedMs;
        LastLightBlurMs = lightBlurMs;
    }

    /// <summary>
    /// Fired when Enabled changes, in case something needs to reallocate
    /// render targets.
    /// </summary>
    public event Action<bool>? OnEnabledChanged;

    public void SetEnabled(bool value)
    {
        if (Enabled == value) return;
        Enabled = value;
        OnEnabledChanged?.Invoke(value);
    }

    /// <summary>
    /// Override the default ambient light for the current scene.
    /// </summary>
    public void SetAmbient(Color color, float intensity = 1f)
    {
        AmbientLight = color * intensity;
    }
}
