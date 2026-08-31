using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Lidgren.Network;

namespace Engine.Shared.Networking;

internal sealed partial class NetManager : INetManager
{
    public NetServer Server { get; private set; }= default!;
    public NetClient Client { get; private set; } = default!;

    private readonly Dictionary<NetConnection, NetSession> _sessions = new();

    public event EventHandler<NetSessionArgs> OnConnected;
    public event EventHandler<NetDisconnectedArgs> OnDisconnected;

    public bool IsServer { get; private set; } = false;

    public bool IsClient { get; private set; } = false;

    public bool IsRunning { get; private set; } = false;

    public IReadOnlyList<INetSession> Sessions => _sessions.Values.ToList();

    public INetSession? ServerSession => Client is not null 
        ? _sessions.Values.FirstOrDefault() : throw new InvalidNetworkSideException("This operation is client-only!");

    // public void Init(bool isServer)
    // {
        
    // }

    public void StartServer(int port)
    {
        var config = BuildConfig();
        config.Port = port;
        Server = new NetServer(config);
        Server.Start();
        IsServer = true;
        IsRunning = true;
    }

    public void ConnectClient(string host, int port)
    {
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
    
    private void PollPeer(NetPeer? peer, NetworkSide side)
    {
        if (peer is null)
            return;

        NetIncomingMessage? msg;
        while ((msg = peer.ReadMessage()) != null)
        {
            switch (msg.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    var status = (NetConnectionStatus)msg.ReadByte();
                    var reason = msg.ReadString();
                    var connection = msg.SenderConnection;
                        
                    AlertClientNewStatus(peer, connection, status, reason);
                    //Log.Debug($"[Net:{side}] ({msg.SenderConnection?.RemoteEndPoint}) {status} ({reason})");
                    break;

                case NetIncomingMessageType.WarningMessage:
                    Log.Warn($"[Net:{side}] {msg.ReadString()}");
                    break;

                case NetIncomingMessageType.ErrorMessage:
                    Log.Error($"[Net:{side}] {msg.ReadString()}");
                    break;

                case NetIncomingMessageType.Data:
                    // todo: game msgs
                    break;
            }

            peer.Recycle(msg);
        }
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
                OnConnected?.Invoke(this, new NetSessionArgs(session));
                break;

            case NetConnectionStatus.Disconnected:
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