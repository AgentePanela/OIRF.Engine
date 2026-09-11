using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Lidgren.Network;

namespace Engine.Shared.Networking;

internal sealed partial class NetManager : INetManager
{
    private readonly Dictionary<string, Func<NetMessage>> _factories = new();
    private readonly Dictionary<string, List<Action<NetMessage, INetSession?>>> _handlers = new();

    public void RegisterNetMessage<T>(Action<T, INetSession?>? rxCallback = null) where T : NetMessage, new()
    {
        var name = typeof(T).FullName!;
        if (!_factories.ContainsKey(name))
            _factories[name] = () => new T();

        if (rxCallback != null)
            (_handlers.TryGetValue(name, out var list) ? list : _handlers[name] = new())
                .Add((msg, session) => rxCallback((T)msg, session));
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

                case NetIncomingMessageType.ConnectionApproval:
                {
                    if (msg.SenderConnection is null)
                        break; //wtf bro :sob:ÇsboS:bo:sob:

                    var args = new NetConnectingArgs(msg.SenderConnection.RemoteEndPoint, msg);
                    if (EngineAuth(args))
                        OnConnecting?.Invoke(this, args); // only invoke if the connection is valid by engine
                                                        // (serialization hash match)

                    if (args.IsDenied)
                        msg.SenderConnection.Deny(args.DenyReason ?? "Connection denied.");
                    else
                        msg.SenderConnection.Approve();

                    break;
                }

                case NetIncomingMessageType.Data:
                {
                    var msgType = msg.ReadString();
                    if (!_factories.TryGetValue(msgType, out var factory))
                    {
                        Log.Error($"Received unknown message type: {msgType}");
                        break;
                    }

                    var netMessage = factory();
                    netMessage.ReadFromBuffer(msg);

                    NetSession? senderSession = null;
                    if (msg.SenderConnection is not null)
                        _sessions.TryGetValue(msg.SenderConnection, out senderSession);

                    if (_handlers.TryGetValue(msgType, out var handlers))
                    {
                        foreach (var handler in handlers)
                        {
                            try
                            {
                                handler(netMessage, senderSession);
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"[Net:{side}] Handler for '{msgType}' threw: {ex}");
                            }
                        }
                    }

                    break;
                }
            }

            peer.Recycle(msg);
        }
    }

    public void Broadcast(NetMessage message, List<INetSession>? specifcSessions = default)
    {
        AssertNetSide(NetworkSide.Server, "This operation is server-side only!");
        specifcSessions ??= Sessions.ToList();

        if (specifcSessions.Count == 0)
            return;

        var outgoing = Server!.CreateMessage();
        outgoing.Write(message.GetType().FullName);
        message.WriteToBuffer(outgoing);

        var recipients = specifcSessions.Select(s => s.Connection).ToList();
        Server.SendMessage(outgoing, recipients, message.DeliveryMethod.ToLidgren(), 0);
    }

}
