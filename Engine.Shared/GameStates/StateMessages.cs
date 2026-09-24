using Engine.Shared.Networking;
using NetDeliveryMethod = Engine.Shared.Networking.NetDeliveryMethod;
using Engine.Shared.Timing;
using Lidgren.Network;

namespace Engine.Shared.GameStates;

/// <summary>
/// Client to server: the newest state the client has applied. It is what the server uses as the baseline of the next
/// state it builds for that session.
/// </summary>
public sealed class StateAckMessage : NetMessage
{
    public GameTick Tick { get; set; } = GameTick.Zero;

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.Unreliable;

    public override void WriteToBuffer(NetOutgoingMessage buffer) => buffer.WriteVariableUInt32(Tick.Value);

    public override void ReadFromBuffer(NetIncomingMessage buffer) => Tick = new GameTick(buffer.ReadVariableUInt32());
}

/// <summary>
/// Client to server: throw the baseline away and send everything again. Sent when the client gets a state it cannot
/// build on top of.
/// </summary>
public sealed class RequestFullStateMessage : NetMessage
{
    public override void WriteToBuffer(NetOutgoingMessage buffer)
    {
    }

    public override void ReadFromBuffer(NetIncomingMessage buffer)
    {
    }
}
