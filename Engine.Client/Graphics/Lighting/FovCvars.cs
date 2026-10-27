using Engine.Shared.Configuration;

namespace Engine.Client.Graphics.Lighting;

[CVarDefs]
public static class FovCvars
{
    /// <summary>
    /// False sees everything, whatever eye the viewports have. Only for debugging.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("fov.enabled", true, CVar.NOSAVE);

    /// <summary>
    /// What is drawn where the eye cannot see, as hex RGBA (#RRGGBBAA). Opaque hides it, translucent darkens it.
    /// </summary>
    public static readonly CVarDef<string> Color =
        CVarDef.Create("fov.color", "#000000FF");
}
