using System;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Central service that owns lighting configuration for the engine.
/// Resolved via IoC: <c>IoCManager.Resolve&lt;LightingManager&gt;()</c> or
/// injected with <c>[Dependency] private readonly LightingManager _lighting;</c>.
/// </summary>
public sealed class LightingManager
{
    /// <summary>
    /// Master toggle. When <c>false</c>, the <see cref="LightingSystem"/>
    /// does nothing in either Update or Draw — zero overhead, the scene
    /// is rendered with raw sprite colors.
    /// </summary>
    public bool Enabled { get; private set; } = false;

    /// <summary>
    /// Default ambient color used when no <see cref="Components.Lighting.AmbientLightComponent"/>
    /// is present in the scene.
    /// </summary>
    public Color AmbientLight { get; set; } = new Color(0, 0, 0);

    /// <summary>
    /// Global multiplier on top of every light's intensity. (1.0 = normal.)
    /// </summary>
    public float LightIntensity { get; set; } = 1f;

    /// <summary>
    /// Hard cap on the number of lights processed per frame. Lights beyond
    /// this cap (sorted by distance to the camera) are skipped.
    /// </summary>
    public int MaxLights { get; set; } = 64;

    /// <summary>
    /// Number of shadow-casting lights allowed per frame. Determines the
    /// height of the shadow map render target. Lights beyond this cap
    /// have shadows disabled (still render as plain lights).
    /// </summary>
    public int MaxShadowcastingLights { get; set; } = 16;

    /// <summary>
    /// Width of the shadow map (in texels) — each row is a 1D unwrapped
    /// 360° view around a single shadow-casting light. 1024 keeps angular
    /// shadows from looking like visible ray slices around small occluders.
    /// </summary>
    public int ShadowMapSize { get; set; } = 1024;

    /// <summary>
    /// Extra shadow bias, in world pixels, subtracted from the blocker depth.
    /// This makes shadows start slightly before the occluder edge so filtered
    /// lighting cannot leave a bright fringe around walls.
    /// </summary>
    public float ShadowContactBias { get; set; } = 1.5f;

    /// <summary>
    /// Multiplier on the soft-shadow kernel size (per-light via
    /// <see cref="Components.Lighting.PointLightComponent.Softness"/>).
    /// Larger values = softer/wider penumbra.
    /// </summary>
    public float LightSoftness { get; set; } = 1.0f;

    /// <summary>
    /// When true, light that "bleeds" through walls is blurred back into
    /// the occluders' footprints, simulating subsurface light scatter /
    /// glow. Off = cheaper, slightly harsher edges.
    /// </summary>
    public bool WallBleedEnabled { get; set; } = false;

    /// <summary>
    /// When true, the accumulated lightmap is blurred (3-pass separable
    /// Gaussian) before being applied to the scene, smoothing shadow
    /// banding and angular aliasing.
    /// </summary>
    public bool LightBlurEnabled { get; set; } = false;

    /// <summary>
    /// Fraction of the virtual viewport used for the lightmap.
    /// 1.0 = native resolution (sharpest, most expensive).
    /// 0.5 = half resolution (blurrier edges, ~4× cheaper fill).
    /// Clamped to [0.1, 1.0].
    /// </summary>
    public float LightmapScale
    {
        get => _lightmapScale;
        set => _lightmapScale = Math.Clamp(value, 0.1f, 1.0f);
    }
    private float _lightmapScale = 1.0f;

    /// <summary>
    /// When <c>true</c>, the lightmap is rendered at one texel per
    /// <see cref="LightPixelSize"/> screen pixels and upscaled with
    /// nearest-neighbour sampling. UV snapping in the shader guarantees
    /// each screen pixel maps to exactly one lightmap texel (no mixels).
    /// </summary>
    public bool PixelatedLighting { get; set; } = false;

    /// <summary>
    /// Size of each light "pixel" in screen pixels when
    /// <see cref="PixelatedLighting"/> is enabled. Clamped to [1, 64].
    /// </summary>
    public int LightPixelSize
    {
        get => _lightPixelSize;
        set => _lightPixelSize = Math.Clamp(value, 1, 64);
    }
    private int _lightPixelSize = 8;

    /// <summary>
    /// When <c>true</c>, the raw lightmap is drawn on top of the screen
    /// instead of being applied. Used for debugging.
    /// </summary>
    public bool DebugDraw { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, shadows use a single shadow-map sample (hard edges)
    /// instead of the 7-tap PCF kernel. Significantly cheaper on the GPU —
    /// use on low-end hardware or when shadow softness is not important.
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
    /// Fired when <see cref="Enabled"/> changes. Useful for systems that
    /// need to (re)allocate render targets.
    /// </summary>
    public event Action<bool>? OnEnabledChanged;

    /// <summary>
    /// Enable or disable the lighting system at runtime.
    /// </summary>
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
