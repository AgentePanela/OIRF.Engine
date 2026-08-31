using System;
using System.Net;
using Lidgren.Network;

namespace Engine.Shared.Networking;

/// <summary>
/// 
/// </summary>
public interface INetSession
{
    public bool IsConnected { get; }
    public string UserName { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public short Ping { get; }

    public void SendMessage(NetMessage message);
    public void Disconnect(string reason);
}

internal sealed class NetSession : INetSession
{
    private readonly NetConnection _connection;

    public NetSession(NetConnection connection) => _connection = connection;

    public bool IsConnected => _connection.Status == NetConnectionStatus.Connected;
    public IPEndPoint RemoteEndPoint => _connection.RemoteEndPoint;
    public short Ping => (short)(_connection.AverageRoundtripTime * 1000);
    public string UserName { get; set; } = "";

    public void SendMessage(NetMessage message)
    {
        
    }
    public void Disconnect(string reason) => _connection.Disconnect(reason);
}

public class NetSessionArgs : EventArgs
{
    public INetSession? Channel { get; }

    public NetSessionArgs(INetSession? channel)
    {
        Channel = channel;
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