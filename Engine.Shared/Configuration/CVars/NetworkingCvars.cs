namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public static class NetworkingCvars
{
    #region General
    /// <summary>
    /// Changes the server listining port. REQUIRES RESTART.
    /// </summary>
    public static CVarDef<int> ServerPort
        = CVarDef.Create("net.port", 1313, CVar.SERVERONLY);

    /// <summary>
    /// How much ticks are simulated per seccound.
    /// </summary>
    public static CVarDef<int> Tickrate
        = CVarDef.Create("net.tickrate", 60, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    /// How many seconds after the last message from the server or a client before we consider it timed out. NO RESTART NEEDED.
    /// </summary>
    public static readonly CVarDef<float> NetConnectionTimeout =
        CVarDef.Create("net.connection-timeout", 25.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Hard max-cap of concurrent connections for the main game networking. NEEDS REBOOTS.
    /// </summary>
    public static readonly CVarDef<int> NetMaxConnections =
        CVarDef.Create("net.max-connections", 128, CVar.REPLICATED | CVar.SERVER);

    #endregion
    #region State/Deltas

    /// <summary>
    /// Turning this off makes every session get a full state every tick. ONLY SET THIS IF YOU ARE DEBUGGING.
    /// </summary>
    public static readonly CVarDef<bool> NetDelta =
        CVarDef.Create("net.delta", true, CVar.SERVERONLY);

    /// <summary>
    /// How many ticks a session has to wait between two full states it asked for.
    /// </summary>
    public static readonly CVarDef<int> NetFullStateCooldown =
        CVarDef.Create("net.full-state-cooldown", 30, CVar.SERVERONLY);

    /// <summary>
    /// How many ticks a session can go without asking for a game state before the next one is sent reliably. so if a client
    /// that is dropping packets is not left behind forever.
    /// </summary>
    public static readonly CVarDef<int> NetForceAckThreshold =
        CVarDef.Create("net.force-ack-after", 60, CVar.SERVERONLY);

    #endregion
    #region Potential Visibility Set (PVS)

    /// <summary>
    /// Turning this off makes every session see its whole room again. ONLY SET THIS IF YOU ARE DEBUGGING
    /// </summary>
    public static readonly CVarDef<bool> NetPvs =
        CVarDef.Create("net.pvs", true, CVar.SERVERONLY);

    /// <summary>
    /// The MAXIMUM a player can see of the world.
    /// </summary>
    public static readonly CVarDef<float> NetPvsRange =
        CVarDef.Create("net.pvs-range", 1536f, CVar.SERVERONLY);

    /// <summary>
    /// Side of a PVS chunk, in world units. Visibility is chunk-granular, so a bigger chunk sends more than needed and
    /// a smaller one makes the query touch more buckets.
    /// </summary>
    public static readonly CVarDef<int> NetPvsChunkSize =
        CVarDef.Create("net.pvs-chunk-size", 512, CVar.SERVERONLY);

    public static readonly CVarDef<float> NetPvsLeaveMargin =
        CVarDef.Create("net.pvs-leave-margin", 512f, CVar.SERVERONLY);

    /// <summary>
    /// How many entities may enter a session's view in a single tick, 0 for no limit.
    /// </summary>
    public static readonly CVarDef<int> NetPvsEnterBudget =
        CVarDef.Create("net.pvs-enter-budget", 200, CVar.SERVERONLY);

    #endregion
    #region Lidgren

    /// <summary>
    /// Whether to attempt UPnP port forwarding automatically - REQUIRES RESTART.
    /// </summary>
    public static readonly CVarDef<bool> NetUPnP =
        CVarDef.Create("net.upnp", false, CVar.SERVERONLY);

    /// <summary>
    /// How often (in seconds) connected peers ping each other to measure round-trip time.
    /// </summary>
    public static readonly CVarDef<float> NetPingInterval =
        CVarDef.Create("net.ping-interval", 4.0f, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    /// Send buffer size on the UDP sockets used for main game networking.
    /// </summary>
    public static readonly CVarDef<int> NetSendBufferSize =
        CVarDef.Create("net.send-buffersize", 131071);

    /// <summary>
    /// Receive buffer size on the UDP sockets used for main game networking.
    /// </summary>
    public static readonly CVarDef<int> NetReceiveBufferSize =
        CVarDef.Create("net.receive-buffersize", 131071);

    #endregion
    #region Lidgren-Debug

    /// <summary>
    /// Simulated chance (0.0 to 1.0) that an outgoing packet is dropped. LOCAL - no reboot needed.
    /// </summary>
    public static readonly CVarDef<float> NetFakeLoss =
        CVarDef.Create("net.fake-loss", 0f);

    /// <summary>
    /// Minimum simulated one-way latency (in seconds) added to outgoing packets. Local - no restart needed.
    /// </summary>
    public static readonly CVarDef<float> NetFakeLagMin =
        CVarDef.Create("net.fake-lag-min", 0f);

    public static readonly CVarDef<float> NetFakeLagRandom =
        CVarDef.Create("net.fake-lag-random", 0f);

    /// <summary>
    /// Simulated chance (0.0 to 1.0) that an outgoing packet is duplicated. Local testing tool, not replicated.
    /// - no restart needed.
    /// </summary>
    public static readonly CVarDef<float> NetFakeDuplicates =
        CVarDef.Create("net.fake-duplicates", 0f);

    #endregion
}