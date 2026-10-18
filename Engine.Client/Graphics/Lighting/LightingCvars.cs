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
}
