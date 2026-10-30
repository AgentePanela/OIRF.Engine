using Engine.Shared.Configuration;

namespace Engine.Client.Graphics.Lighting;

[CVarDefs]
public static class LightingCvars
{
    public static readonly CVarDef<float> Resolution =
        CVarDef.Create("light.resolution", 0.5f);

    public static readonly CVarDef<bool> Pixelated =
        CVarDef.Create("light.pixelated", false);

    public static readonly CVarDef<float> PixelSize =
        CVarDef.Create("light.pixel-size", 1f);

    /// <summary>
    /// How much brighter than drawn the light can make something.
    /// </summary>
    public static readonly CVarDef<float> Overbright =
        CVarDef.Create("light.overbright", 2f);

    public static readonly CVarDef<bool> Dither =
        CVarDef.Create("light.dither", true);

    /// <summary>
    /// How much the light is rendered off bounds in pxs.
    /// </summary>
    public static readonly CVarDef<float> Margin =
        CVarDef.Create("light.margin", 128f);

    public static readonly CVarDef<int> MaxSize =
        CVarDef.Create("light.max-size", 4096);

    public static readonly CVarDef<float> SoftnessScale =
        CVarDef.Create("light.softness-scale", 8f);

    /// <summary>
    /// Keeps the light of what does not change instead of drawing every light every frame. False is only to compare.
    /// </summary>
    public static readonly CVarDef<bool> Cache =
        CVarDef.Create("light.cache", true);

    /// <summary>
    /// Seconds a light has to stay the same (and the occluders it reaches) before it is kept instead of drawn every frame.
    /// </summary>
    public static readonly CVarDef<float> StaticDelay =
        CVarDef.Create("light.static-delay", 0.5f);

    public static readonly CVarDef<bool> Bloom =
        CVarDef.Create("light.bloom", false);

    /// <summary>
    /// 0 to 1, the brightness the bloom glow starts at.
    /// </summary>
    public static readonly CVarDef<float> BloomThreshold =
        CVarDef.Create("light.bloom-threshold", 0.8f);

    public static readonly CVarDef<float> BloomIntensity =
        CVarDef.Create("light.bloom-intensity", 0.5f);

    public static readonly CVarDef<bool> DebugBase =
        CVarDef.Create("light.debug-base", false, CVar.NOSAVE);

    public static readonly CVarDef<bool> DebugOccluders =
        CVarDef.Create("light.occluder-overlay", false, CVar.NOSAVE);
    
    public static readonly CVarDef<string> FovColor =
        CVarDef.Create("fov.color", "#000000FF");
}
