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
    /// Get the connected client session (CLIENT-SIDE)
    /// </summary>
    public INetSession? MySession { get; }

    /// <summary>
    /// The SessionId the server assigned to this connection (CLIENT-SIDE).
    /// Null until <see cref="ClientHandshakeMessage"/> arrives, right after connecting -
    /// this is the id that will key you into replicated game state later on,
    /// not <c>MySession.SessionId</c> (that one's just a local artifact).
    /// </summary>
    public string? MySessionId { get; }

    // public void Init(bool isServer);

    /// <summary>
    /// Starts a server.
    /// </summary>
    public void StartServer(int port);

    /// <summary>
    /// Opens a connection to the desired server (CLIENT-SIDE)
    /// </summary>
    public void ConnectClient(string host, int port);

    /// <summary>
    /// Disconnects the client from the server or the server from all clients.
    /// </summary>
    /// <param name="reason">The reason for this disconnection.</param>    
    public void Shutdown(string reason);

    internal void Update();

    /// <summary>
    /// Register a callback for a message receiving event.
    /// </summary>
    /// <typeparam name="T">MessageType.</typeparam>
    /// <param name="rxCallback">Callback function. The session is whoever the message physically arrived from - never trust a session id the message payload itself might claim.</param>
    public void RegisterNetMessage<T>(Action<T, INetSession?>? rxCallback = null) where T : INetMessage, new();

    public event EventHandler<NetSessionArgs> OnConnected;
    public event EventHandler<NetDisconnectedArgs> OnDisconnected;
}
