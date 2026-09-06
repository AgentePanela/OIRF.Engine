using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lidgren.Network;

namespace Engine.Shared.Networking;

/// <summary>
/// Manages networking operations client and server side.
/// </summary>
public interface INetManager
{
    public NetServer? Server { get; }
    public NetClient? Client { get; }
    
    [MemberNotNullWhen(true, nameof(Server))]
    public bool IsServer { get; }

    [MemberNotNullWhen(true, nameof(Client))]
    public bool IsClient { get; }
    public bool IsRunning { get; }

    /// <summary>
    /// All current connected sessions.
    /// </summary>
    /// <remarks>
    /// Client will only have the <seealso cref="MySession"/>!
    /// </remarks>
    public IReadOnlyList<INetSession> Sessions { get; }

    /// <summary>
    /// Looks up a connected session by its <see cref="INetSession.SessionId"/>.
    /// Null if no session with that id is currently connected.
    /// </summary>
    public INetSession? GetSessionById(string sessionId);

    /// <summary>
    /// Starts a server.
    /// </summary>
    public void StartServer(int port);

    /// <summary>
    /// Get the connected client session (CLIENT-SIDE)
    /// </summary>
    public INetSession? MySession { get; }

    /// <summary>
    /// The SessionId the server assigned to this connection (CLIENT-SIDE).
    /// </summary>
    public string? MySessionId { get; }

    // public void Init(bool isServer);

    /// <summary>
    /// Opens a connection to the desired server (CLIENT-SIDE)
    /// </summary>
    public void ConnectClient(string host, int port);

    /// <summary>
    /// Disconnects the client with a reason. (CLIENT-SIDE)
    /// </summary>
    public void DisconnectClient(string reason);

    /// <summary>
    /// Disconnects the client from the server or the server from all clients.
    /// </summary>
    /// <param name="reason">The reason for this disconnection.</param>    
    public void Shutdown(string reason);

    internal void Update();

    /// <summary>
    /// Send a message to all (or specific) connected clients in the server. (SERVER-SIDE)
    /// </summary>
    /// <param name="specifcSessions">Optional list of clients to broadcast the message.</param>
    public void Broadcast(INetMessage message, List<INetSession>? specifcSessions = default);

    /// <summary>
    /// Register a callback for a message receiving event.
    /// </summary>
    /// <typeparam name="T">MessageType.</typeparam>
    /// <param name="rxCallback">Callback function. The session is whoever the message physically arrived from - never trust a session id the message payload itself might claim.</param>
    public void RegisterNetMessage<T>(Action<T, INetSession?>? rxCallback = null) where T : INetMessage, new();

    public event EventHandler<NetSessionArgs> OnConnected;
    public event EventHandler<NetDisconnectedArgs> OnDisconnected;
}
