using Engine.Shared.Networking;
using NetDeliveryMethod = Engine.Shared.Networking.NetDeliveryMethod;
using Lidgren.Network;

namespace Engine.Shared.GameStates;

/// <summary>
/// One tick of the world, as ONE session is allowed to see it.
/// </summary>
public sealed class GameStateMessage : NetMessage
{
    /// <summary>
    /// States are unreliable a lost one is replaced by the next tick!
    /// <see cref="NetDeliveryMethod.ReliableOrdered"/> for the states that cannot be lost, see like a entity creation
    /// </summary>
    public NetDeliveryMethod Delivery = NetDeliveryMethod.Unreliable;

    public override NetDeliveryMethod DeliveryMethod => Delivery;

    /// <summary>
    /// Sending side: the body, already written while the state was built - see PvsBuildContext.
    /// </summary>
    public byte[] Body = [];
    public int BodyLength;

    /// <summary>
    /// Receiving side: the header that came in.
    /// </summary>
    public GameState State = new();

    /// <summary>
    /// Receiving side only. A component payload has no length of its own, so the blocks cannot be picked apart
    /// here so they are parsed and applied in one pass, later, by whoever owns the entities.
    /// </summary>
    public byte[] Blocks = [];
    public int BlocksLength;
    public int BlockCount;

    public override void WriteToBuffer(NetOutgoingMessage buffer)
        => buffer.Write(Body, 0, BodyLength);

    public override void ReadFromBuffer(NetIncomingMessage buffer)
    {
        GameStateSerializer.ReadHeader(buffer, State, out BlockCount);

        buffer.ReadPadBits();
        BlocksLength = buffer.LengthBytes - buffer.PositionInBytes;
        Blocks = buffer.ReadBytes(BlocksLength);
    }
}
