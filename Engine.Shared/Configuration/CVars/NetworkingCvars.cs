namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public static class NetworkingCvars
{
    /// <summary>
    /// Changes the server listining port. REQUIRES RESTART.
    /// </summary>
    public static CVarDef<int> ServerPort
        = CVarDef.Create("net.port", 1313, CVar.SERVERONLY);

    public static CVarDef<int> Tickrate
        = CVarDef.Create("net.tickrate", 60, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    /// How many seconds after the last message from the server or a client before we consider it timed out.
    /// Applies live to already-running peers (NetManager subscribes to it) - no restart needed.
    /// </summary>
    public static readonly CVarDef<float> NetConnectionTimeout =
        CVarDef.Create("net.connection_timeout", 25.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Hard max-cap of concurrent connections for the main game networking. Lidgren locks this once the
    /// peer has started, so changing it only takes effect on the next StartServer call.
    /// </summary>
    public static readonly CVarDef<int> NetMaxConnections =
        CVarDef.Create("net.max_connections", 128, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    /// Simulated chance (0.0 to 1.0) that an outgoing packet is dropped. Local testing tool, not replicated -
    /// set it on whichever side (client or server) you want to simulate bad conditions for.
    /// Applies live to an already-running peer - no restart needed.
    /// </summary>
    public static readonly CVarDef<float> NetFakeLoss =
        CVarDef.Create("net.fake_loss", 0f);

    /// <summary>
    /// Minimum simulated one-way latency (in seconds) added to outgoing packets. Local testing tool, not replicated.
    /// Applies live to an already-running peer - no restart needed.
    /// </summary>
    public static readonly CVarDef<float> NetFakeLagMin =
        CVarDef.Create("net.fake_lag_min", 0f);

    /// <summary>
    /// Extra random one-way latency (in seconds, uniform between 0 and this value on top of
    /// <see cref="NetFakeLagMin"/>) added to outgoing packets. Local testing tool, not replicated.
    /// Applies live to an already-running peer - no restart needed.
    /// </summary>
    public static readonly CVarDef<float> NetFakeLagRandom =
        CVarDef.Create("net.fake_lag_random", 0f);

    /// <summary>
    /// Simulated chance (0.0 to 1.0) that an outgoing packet is duplicated. Local testing tool, not replicated.
    /// Applies live to an already-running peer - no restart needed.
    /// </summary>
    public static readonly CVarDef<float> NetFakeDuplicates =
        CVarDef.Create("net.fake_duplicates", 0f);

    /// <summary>
    /// Whether to attempt UPnP port forwarding automatically (useful for self-hosted servers behind a router
    /// with no manual port-forward set up). Lidgren locks this once the peer has started - REQUIRES RESTART.
    /// </summary>
    public static readonly CVarDef<bool> NetUPnP =
        CVarDef.Create("net.upnp", false, CVar.SERVERONLY);

    /// <summary>
    /// How often (in seconds) connected peers ping each other to measure round-trip time.
    /// Applies live to already-running peers (NetManager subscribes to it) - no restart needed.
    /// </summary>
    public static readonly CVarDef<float> NetPingInterval =
        CVarDef.Create("net.ping_interval", 4.0f, CVar.REPLICATED | CVar.SERVER);
}