using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.Networking;
using Engine.Shared.Timing;

namespace Engine.Server.GameStates;

/// <summary>
/// What the server remembers about one session between ticks: where its state stream is, and what it already has.
/// </summary>
public sealed class PvsSession(INetSession session)
{
    public readonly INetSession Session = session;

    /// <summary>
    /// The newest tick this session said it applied.
    /// </summary>
    public GameTick LastReceivedAck = GameTick.Zero;

    /// <summary>
    /// The session has nothing, or asked for everything again.
    /// </summary>
    public bool RequestedFull = true;

    /// <summary>
    /// The next state cannot be dropped on the way - see <see cref="GameStateMessage.Delivery"/>.
    /// </summary>
    public bool ForceSendReliably = true;

    /// <summary>
    /// When each visible entity was last sent to this session. An entity only counts as known once the session acks
    /// a tick at or after that
    /// </summary>
    public readonly Dictionary<NetEntity, GameTick> Sent = new();

    /// <summary>
    /// The chunks this session saw last tick.
    /// </summary>
    public readonly HashSet<PvsChunkKey> SeenChunks = new();

    public readonly HashSet<EntityUid> Viewers = new();

    public bool Knows(NetEntity netEntity)
        => Sent.TryGetValue(netEntity, out var sentAt) && sentAt <= LastReceivedAck;

    /// <summary>
    /// When the last full state this session asked for was granted.
    /// </summary>
    public GameTick LastFullStateRequest = GameTick.Zero;

    /// <summary>
    /// How many full state requests were turned down for asking too fast.
    /// </summary>
    public int IgnoredFullStateRequests;

    /// <summary>
    /// The scratch this session state is built in, so the build can run off the main thread.
    /// </summary>
    public readonly PvsBuildContext Build = new();
}
