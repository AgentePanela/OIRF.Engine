using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using Lidgren.Network;

namespace Engine.Shared.Networking;

internal sealed partial class NetManager : INetManager
{
    public NetServer? Server { get; private set; }= default;
    public NetClient? Client { get; private set; } = default;

    private readonly Dictionary<NetConnection, NetSession> _sessions = new();

    public event EventHandler<NetSessionArgs> OnConnected;
    public event EventHandler<NetDisconnectedArgs> OnDisconnected;

    [MemberNotNullWhen(true, nameof(Server))]
    public bool IsServer { get; private set; } = false;

    [MemberNotNullWhen(true, nameof(Client))]
    public bool IsClient { get; private set; } = false;

    public bool IsRunning { get; private set; } = false;

    public IReadOnlyList<INetSession> Sessions => _sessions.Values.ToList();

    public INetSession? GetSessionById(string sessionId)
        => _sessions.Values.FirstOrDefault(s => s.SessionId == sessionId);

    public NetManager()
    {
        RegisterNetMessage<ClientHandshakeMessage>(ClientHandshakeCompleted);
    }

    // public void Init(bool isServer)
    // {

    // }

    public void StartServer(int port)
    {
        if (IsServer)
            throw new InvalidOperationException("A server is already running.");

        var config = BuildConfig();
        config.Port = port;
        Server = new NetServer(config);
        Server.Start();
        IsServer = true;
        IsRunning = true;
    }

    public void ConnectClient(string host, int port)
    {
        if (IsClient)
            throw new InvalidOperationException("Already connected (or connecting) to a server.");

        Log.Debug($"Attempting to connect to {host} port {port}...");
        var config = BuildConfig();
        Client = new(config);
        Client.Start();
        Client.Connect(host, port);
        IsClient = true;
        IsRunning = true;
    }

    private NetPeerConfiguration BuildConfig()
    {
        var config = new NetPeerConfiguration("OIRF");
        config.EnableMessageType(NetIncomingMessageType.StatusChanged);
        config.EnableMessageType(NetIncomingMessageType.WarningMessage);
        config.EnableMessageType(NetIncomingMessageType.ErrorMessage);

        return config;
    }

    void INetManager.Update()
    {
        PollPeer(Server, NetworkSide.Server);
        PollPeer(Client, NetworkSide.Client);
    }

    private void AlertClientNewStatus(NetPeer peer, NetConnection? connection, NetConnectionStatus status, string? reason)
    {
        switch (status)
        {
            case NetConnectionStatus.Connected:
                if (connection is null)
                    return;
                
                var session = new NetSession(connection);
                _sessions[connection] = session;

                if (peer == Server) 
                {
                    session.SendMessage(new ClientHandshakeMessage(session.SessionId));
                    OnConnected?.Invoke(this, new NetSessionArgs(session)); // client connection invoke is sent when handshake is received
                    Log.Debug($"");
                }
                break;

            case NetConnectionStatus.Disconnected:
                if (peer == Client)
                        OnClientDisconnect();

                if (connection is not null)
                {
                    if (_sessions.Remove(connection, out var removed))
                        OnDisconnected?.Invoke(this, new NetDisconnectedArgs(removed, reason));
                } // ts or c# nukes my else if
                else if (peer == Client)// client connection failed
                    OnDisconnected?.Invoke(this, new NetDisconnectedArgs(default, reason));
                break;
        }
    }

    public void Shutdown(string reason)
    {
        IsRunning = false;
        if (IsServer)
            Server.Shutdown(reason);

        if (IsClient)
            Client.Shutdown(reason);
    }

    public bool AssertNetSide(NetworkSide side, string reason = "This operation is client-only!")
    {
        var valid = side switch
        {
            NetworkSide.Client => IsClient,
            NetworkSide.Server => IsServer,
            _ => false,
        };

        if (!valid)
            throw new InvalidNetworkSideException(reason);

        return true;
    }
}

/// <summary>
/// Your networking side (client / server) is not valid for this operation.
/// </summary>
public class InvalidNetworkSideException : Exception
{
    public InvalidNetworkSideException() { }
    public InvalidNetworkSideException(string message) : base(message) { }
    public InvalidNetworkSideException(string message, Exception inner) : base(message, inner) { }
}

public enum NetworkSide
{
    Client,
    Server
}