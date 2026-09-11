using System;
using System.Linq;
using Lidgren.Network;

namespace Engine.Shared.Networking;

internal sealed partial class NetManager : INetManager
{
    public INetSession? MySession
    {
        get
        {
            AssertNetSide(NetworkSide.Client);
            return _sessions.Values.FirstOrDefault();
        }
    }

    public string? MySessionId => MySession?.SessionId;
    
    private void ClientHandshakeCompleted(ClientHandshakeMessage msg, INetSession? session)
    {
        MySession?.ForceSessionId(msg.SessionId);
        OnConnected?.Invoke(this, new NetSessionArgs(session));
        Log.Debug("Handshake completed, connection established.");
    }

    private void OnClientDisconnect()
    {
        Client = null;
        IsClient = false;
    }

    public void ConnectClient(string host, int port, NetMessage? hailMessage = null)
    {
        if (IsClient)
            throw new InvalidOperationException("Already connected (or connecting) to a server.");

        Log.Debug($"Attempting to connect to {host} port {port}...");
        var config = BuildConfig();
        Client = new(config);
        Client.Start();

        var msg = Client.CreateMessage();
        msg.Write(_seriMan.GetHash());
        msg.Write(hailMessage is not null);
        if (hailMessage is not null)
            hailMessage?.WriteToBuffer(msg);

        Client.Connect(host, port, msg);
        IsClient = true;
        IsRunning = true;
    }

    public void DisconnectClient(string reason)
    {
        AssertNetSide(NetworkSide.Client);
        MySession?.Disconnect(reason);
    }
}

/// <summary>
/// Sent by the server to a client right after its <see cref="NetSession"/> is created.
/// </summary>
public sealed partial class ClientHandshakeMessage : NetMessage
{
    public string SessionId { get; private set; } = "";

    public ClientHandshakeMessage(string sessionId)
    {
        SessionId = sessionId;
    }
}