namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public static class UiCvars
{
    public static readonly CVarDef<bool> ResAutoScaleEnabled =
        CVarDef.Create("ui.resolutionAutoScaleEnabled", true, CVar.CLIENTONLY);

    /// <summary>
    /// Window width at/above which the UI renders at scale 1.
    /// </summary>
    public static readonly CVarDef<int> ResAutoScaleUpperX =
        CVarDef.Create("ui.resolutionAutoScaleUpperCutoffX", 1080, CVar.CLIENTONLY);

    /// <summary>
    /// Window height at/above which the UI renders at scale 1.
    /// </summary>
    public static readonly CVarDef<int> ResAutoScaleUpperY =
        CVarDef.Create("ui.resolutionAutoScaleUpperCutoffY", 720, CVar.CLIENTONLY);

    /// <summary>
    /// Window width at/below which the UI is clamped to <see cref="ResAutoScaleMin"/>.
    /// </summary>
    public static readonly CVarDef<int> ResAutoScaleLowX =
        CVarDef.Create("ui.resolutionAutoScaleLowerCutoffX", 520, CVar.CLIENTONLY);

    /// <summary>
    /// Window height at/below which the UI is clamped to <see cref="ResAutoScaleMin"/>.
    /// </summary>
    public static readonly CVarDef<int> ResAutoScaleLowY =
        CVarDef.Create("ui.resolutionAutoScaleLowerCutoffY", 520, CVar.CLIENTONLY);

    /// <summary>
    /// The smallest scale auto-scale will ever clamp down to.
    /// </summary>
    public static readonly CVarDef<float> ResAutoScaleMin =
        CVarDef.Create("ui.resolutionAutoScaleMinimum", 0.65f, CVar.CLIENTONLY);

    /// <summary>
    /// Manual UI scale multiplier, on top of the automatic resolution-based one. 
    /// </summary>
    public static readonly CVarDef<float> Scale =
        CVarDef.Create("ui.scale", 1f, CVar.CLIENTONLY);
}
