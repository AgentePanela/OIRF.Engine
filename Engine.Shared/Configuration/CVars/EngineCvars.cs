namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public sealed class EngineCvars
{
    public static readonly CVarDef<string> EngineVersion =
        CVarDef.Create("engine.version", "1.0.0 IN-DEV");

    public static readonly CVarDef<int> SystemProfillerTop =
        CVarDef.Create("engine.system-profiller-top", 10);

    /// <summary>
    /// How often (in seconds) an open View Variables window re-asks the server for a snapshot. 0 means only when the
    /// Refresh button is pressed.
    /// </summary>
    public static readonly CVarDef<float> VVRemoteRefresh =
        CVarDef.Create("vv.remote-refresh", 0.25f, CVar.CLIENTONLY);
}
