using System;
using System.Linq;

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
}