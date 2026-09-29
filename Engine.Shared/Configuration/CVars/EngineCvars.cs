namespace Engine.Shared.Configuration.CVars;

// put every "random" system more "deep" config (like transform max parenting) here.

[CVarDefs]
public sealed class EngineCvars
{
    public static readonly CVarDef<string> EngineVersion =
        CVarDef.Create("engine.version", "1.0.0 IN-DEV", CVar.NOSAVE);

    public static readonly CVarDef<int> SystemProfillerTop =
        CVarDef.Create("engine.system-profiller-top", 10);

    public static readonly CVarDef<int> TransformMaxParents =
        CVarDef.Create("engine.transform-max-parenting", 32);

    /// <summary>
    /// How often (in seconds) an open View Variables window re-asks the server for a snapshot. 0 means only when the
    /// Refresh button is pressed.
    /// </summary>
    public static readonly CVarDef<float> VVRemoteRefresh =
        CVarDef.Create("vv.remote-refresh", 0.25f, CVar.CLIENTONLY);
}
