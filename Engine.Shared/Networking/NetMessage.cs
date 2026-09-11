using Lidgren.Network;

namespace Engine.Shared.Networking;

/// <summary>
/// A message sendable over the network.
/// </summary>
public abstract class NetMessage
{
    /// <summary>
    /// How this message should be delivered.
    /// </summary>
    public virtual NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;

    public abstract void WriteToBuffer(NetOutgoingMessage buffer);
    public abstract void ReadFromBuffer(NetIncomingMessage buffer);
}
