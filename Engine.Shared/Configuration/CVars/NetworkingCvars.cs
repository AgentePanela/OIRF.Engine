namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public static class NetworkingCvars
{
    /// <summary>
    /// Changes the server listining port. REQUIRES RESTART.
    /// </summary>
    public static CVarDef<int> ServerPort
        = CVarDef.Create("net.port", 1313, CVar.SERVERONLY);

    public static CVarDef<float> Tickrate
        = CVarDef.Create("net.tickrate", 60f, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    /// How many seconds after the last message from the server or a client before we consider it timed out.
    /// </summary>
    public static readonly CVarDef<float> NetConnectionTimeout =
        CVarDef.Create("net.connection_timeout", 25.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Hard max-cap of concurrent connections for the main game networking.
    /// </summary>
    public static readonly CVarDef<int> NetMaxConnections =
        CVarDef.Create("net.max_connections", 128, CVar.REPLICATED | CVar.SERVER);
}