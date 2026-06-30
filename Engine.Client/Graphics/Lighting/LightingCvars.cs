using Engine.Shared.Configuration;

namespace Engine.Client.Graphics.Lighting;

[CVarDefs]
public static class LightingCvars
{
    public static readonly CVarDef<float> LightmapScale =
        CVarDef.Create("lighting.lightmap-scale", 1.0f);

    public static readonly CVarDef<bool> PixelatedLighting =
        CVarDef.Create("lighting.pixelated", false);

    /// <summary>
    /// When PixelatedLighting is on, each lightmap texel covers this many
    /// screen pixels on each axis (e.g. 8 → 8×8 pixel light blocks).
    /// </summary>
    public static readonly CVarDef<int> LightPixelSize =
        CVarDef.Create("lighting.pixel-size", 8);
}
