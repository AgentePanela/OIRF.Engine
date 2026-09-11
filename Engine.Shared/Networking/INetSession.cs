using System;
using System.Net;
using Lidgren.Network;

namespace Engine.Shared.Networking;

/// <summary>
/// Represents a user (or the server) connection.
/// </summary>
public interface INetSession
{
    public string SessionId { get; }
    public bool IsConnected { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public short Ping { get; }

    public void SendMessage(NetMessage message);
    public void Disconnect(string reason);
    internal void ForceSessionId(string id);
    internal NetConnection Connection { get; }
}

internal sealed class NetSession : INetSession
{
    private readonly NetConnection _connection;

    public NetSession(NetConnection connection)
    {
        _connection = connection;
        SessionId = Guid.NewGuid().ToString()[..8];
    }

    public string SessionId { get; private set; }
    public bool IsConnected => _connection.Status == NetConnectionStatus.Connected;
    public IPEndPoint RemoteEndPoint => _connection.RemoteEndPoint;
    public short Ping => (short)(_connection.AverageRoundtripTime * 1000);

    public void SendMessage(NetMessage message)
    {
        var outgoing = _connection.Peer.CreateMessage();
        outgoing.Write(message.GetType().FullName); // message header
        message.WriteToBuffer(outgoing); // generatedd
        _connection.Peer.SendMessage(outgoing, _connection, message.DeliveryMethod.ToLidgren());
    }

    public void Disconnect(string reason) => _connection.Disconnect(reason);

    NetConnection INetSession.Connection => _connection;

    public override string ToString() => $"{SessionId}";

    void INetSession.ForceSessionId(string id)
    {
        SessionId = id;
    }
}

public class NetSessionArgs : EventArgs
{
    public INetSession? Session { get; }

    public NetSessionArgs(INetSession? session)
    {
        Session = session;
    }
}

public sealed class NetDisconnectedArgs : NetSessionArgs
{
    public string? Reason { get; }

    public NetDisconnectedArgs(INetSession? session, string? reason) : base(session)
    {
        Reason = reason;
    }
}
