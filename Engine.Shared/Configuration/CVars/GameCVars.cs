namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public static class GameCVars
{
    public static CVarDef<string> GameVersion 
        = CVarDef.Create("game.version", "");

    public static CVarDef<int> ResolutionWidth
        = CVarDef.Create("game.resolution-witdh", 0);

    public static CVarDef<int> ResolutionHeight
        = CVarDef.Create("game.resolution-height", 0);

    public static CVarDef<bool> ScaleOuter
        = CVarDef.Create("game.scale", true);

    public static CVarDef<bool> Vsync
        = CVarDef.Create("game.vsync", true);

    /// <summary>
    /// When enabled, limits the framerate to the <seealso cref="FramerateLimit"/> value.
    /// </summary>
    public static CVarDef<bool> FixedTimestep
        = CVarDef.Create("game.fixed-timestep", true);

    public static CVarDef<int> FramerateLimit
        = CVarDef.Create("game.framerate-limit", 60);
}
