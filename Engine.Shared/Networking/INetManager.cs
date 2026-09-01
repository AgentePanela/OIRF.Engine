using System;
using System.Collections.Generic;
using Lidgren.Network;

namespace Engine.Shared.Networking;

/// <summary>
/// Manages networking operations client and server side.
/// </summary>
public interface INetManager
{
    public NetServer Server { get; }
    public NetClient Client { get; }
    
    public bool IsServer { get; }
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
    /// Get the connected client session (CLIENT-SIDE)
    /// </summary>
    public INetSession? MySession { get; }

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
    /// <param name="rxCallback">Callback function.</param>
    public void RegisterNetMessage<T>(Action<T>? rxCallback = null) where T : INetMessage, new();

    public event EventHandler<NetSessionArgs> OnConnected;
    public event EventHandler<NetDisconnectedArgs> OnDisconnected;
}
