using System;
using System.Net;
using Lidgren.Network;

namespace Engine.Shared.Networking;

internal sealed partial class NetManager : INetManager
{
    public event EventHandler<NetConnectingArgs>? OnConnecting;

    private bool EngineAuth(NetConnectingArgs args)
    {
        var serverHash = _seriMan.GetHash();
        if (args.SerializerHash != serverHash)
        {
            args.Deny(Loc.GetString("engine-netman-server-hail-fail-hash-not-sync-reason"));
            return false;
        }
        
        args.Approve();
        return true;
    }
}

/// <summary>
/// Arguments that contains client info before connecting to the server. 
/// </summary>
public sealed class NetConnectingArgs : EventArgs
{
    public IPEndPoint RemoteEndPoint { get; }
    public string SerializerHash { get; }
    public bool IsDenied { get; private set; }
    public string? DenyReason { get; private set; }

    private NetIncomingMessage _rawMessage { get; }
    private NetMessage? _authMessage;
    private bool _hasMessage = false;

    internal NetConnectingArgs(IPEndPoint remoteEndPoint, NetIncomingMessage rawMessage)
    {
        RemoteEndPoint = remoteEndPoint;
        SerializerHash = rawMessage.ReadString();
        _hasMessage = rawMessage.ReadBoolean();
        _rawMessage = rawMessage;
    }

    /// <summary>
    /// Reads the hail message. Do how many times you want.
    /// Returns null if no auth message was give.
    /// </summary>
    public T? ReadAuthMessage<T>() where T : NetMessage, new()
    {
        if (!_hasMessage)
            return null;
        
        if (_authMessage is not null)
            return (T)_authMessage;
        
        var hail = new T();
        hail.ReadFromBuffer(_rawMessage);
        _authMessage = hail;
        return hail;
    }

    public void Deny(string reason)
    {
        IsDenied = true;
        DenyReason = reason;
    }

    // Do nothing award
    public void Approve() {}
}
