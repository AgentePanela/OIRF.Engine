namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public static class GameCVars
{
    public static CVarDef<string> GameVersion 
        = CVarDef.Create("game.version", "");

    public static CVarDef<int> ResolutionWidth
        = CVarDef.Create("game.resolution-witdh", 0, CVar.CLIENTONLY);

    public static CVarDef<int> ResolutionHeight
        = CVarDef.Create("game.resolution-height", 0, CVar.CLIENTONLY);

    /// <summary>
    /// When enabled, the viewport will fill the entire screen, at the cost of making part of the original viewport
    /// off bounds.
    /// </summary>
    public static CVarDef<bool> FitScaleOuter
        = CVarDef.Create("game.fit-outer", true, CVar.CLIENTONLY);

    /// <summary>
    /// When enabled, the viewport scale is snapped to the nearest integer, better for 
    /// look at pixel art games.
    /// </summary>
    public static CVarDef<bool> FitScaleInteger
        = CVarDef.Create("game.fit-int", false, CVar.CLIENTONLY);

    public static CVarDef<bool> Vsync
        = CVarDef.Create("game.vsync", true, CVar.CLIENTONLY);

    /// <summary>
    /// When enabled, limits the framerate to the <seealso cref="FramerateLimit"/> value.
    /// </summary>
    public static CVarDef<bool> FixedTimestep
        = CVarDef.Create("game.fixed-timestep", true, CVar.CLIENTONLY);

    public static CVarDef<int> FramerateLimit
        = CVarDef.Create("game.framerate-limit", 60, CVar.CLIENTONLY);

    public static CVarDef<int> ConsoleSuggestions
        = CVarDef.Create("game.console-suggestions", 8, CVar.CLIENTONLY);
}
