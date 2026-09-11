using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.IoC;
using Engine.Shared.Serializer;
using Lidgren.Network;

namespace Engine.Shared.Networking;

internal sealed partial class NetManager : INetManager
{
    [Dependency] private readonly ISerializationManager _seriMan = default!;
    [Dependency] private readonly IConfigurationManager _configMan = default!; // registered before INetManager (see SharedContentManager.Init) - safe to resolve here
    [Dependency] private readonly SharedContentManager _sharedContent = default!; // registered even earlier, by GameServer/GameClient - also safe here
    
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

    public NetManager()
    {
        IoCManager.ResolveDependencies(this);
        RegisterNetMessage<ClientHandshakeMessage>(ClientHandshakeCompleted);

        SubscribeLiveConfig(NetworkingCvars.NetFakeLoss, (c, v) => c.SimulatedLoss = v);
        SubscribeLiveConfig(NetworkingCvars.NetFakeLagMin, (c, v) => c.SimulatedMinimumLatency = v);
        SubscribeLiveConfig(NetworkingCvars.NetFakeLagRandom, (c, v) => c.SimulatedRandomLatency = v);
        SubscribeLiveConfig(NetworkingCvars.NetFakeDuplicates, (c, v) => c.SimulatedDuplicatesChance = v);
        SubscribeLiveConfig(NetworkingCvars.NetPingInterval, (c, v) => c.PingInterval = v);
        SubscribeLiveConfig(NetworkingCvars.NetConnectionTimeout, (c, v) => c.ConnectionTimeout = v);
    }

    private void SubscribeLiveConfig<T>(CVarDef<T> cvar, Action<NetPeerConfiguration, T> apply)
    {
        _configMan.Subs(cvar, value =>
        {
            if (Server is not null)
                apply(Server.Configuration, value);

            if (Client is not null)
                apply(Client.Configuration, value);
        }, invokeImmediately: false);
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

    public INetSession? GetSessionById(string sessionId)
        => _sessions.Values.FirstOrDefault(s => s.SessionId == sessionId);

    private NetPeerConfiguration BuildConfig()
    {
        var config = new NetPeerConfiguration("OIRF");
        config.EnableMessageType(NetIncomingMessageType.StatusChanged);
        config.EnableMessageType(NetIncomingMessageType.WarningMessage);
        config.EnableMessageType(NetIncomingMessageType.ErrorMessage);
        config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

        config.ConnectionTimeout = _configMan.Get(NetworkingCvars.NetConnectionTimeout);
        config.MaximumConnections = _configMan.Get(NetworkingCvars.NetMaxConnections);

        config.SimulatedLoss = _configMan.Get(NetworkingCvars.NetFakeLoss);
        config.SimulatedMinimumLatency = _configMan.Get(NetworkingCvars.NetFakeLagMin);
        config.SimulatedRandomLatency = _configMan.Get(NetworkingCvars.NetFakeLagRandom);
        config.SimulatedDuplicatesChance = _configMan.Get(NetworkingCvars.NetFakeDuplicates);

        config.PingInterval = _configMan.Get(NetworkingCvars.NetPingInterval);

        // SERVERONLY
        if (_sharedContent.IsServer())
            config.EnableUPnP = _configMan.Get(NetworkingCvars.NetUPnP);

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
                }
                break;

            case NetConnectionStatus.Disconnected:
                if (connection is not null)
                {
                    if (_sessions.Remove(connection, out var removed))
                        OnDisconnected?.Invoke(this, new NetDisconnectedArgs(removed, reason));
                    else if (peer == Client) // connection never finished handshaking (e.g. denied on approval)
                        OnDisconnected?.Invoke(this, new NetDisconnectedArgs(default, reason));
                }
                else if (peer == Client) // client connection failed outright (unreachable host, etc.)
                    OnDisconnected?.Invoke(this, new NetDisconnectedArgs(default, reason));

                if (peer == Client)
                        OnClientDisconnect();
                break;
        }
    }

    public void Shutdown(string reason)
    {
        IsRunning = false;

        if (IsServer)
        {
            Server.Shutdown(reason);
            Server = null;
            IsServer = false;
        }

        if (IsClient)
        {
            Client.Shutdown(reason); 
            // Client = null; //already resolved by client-side disconnected event.
            // IsClient = false;
        }
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