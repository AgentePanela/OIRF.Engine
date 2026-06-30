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
}
