using Lidgren.Network;

namespace Engine.Shared.Networking;

public abstract class NetMessage
{
    public abstract void WriteToBuffer(NetOutgoingMessage buffer);
    public abstract void ReadFromBuffer(NetIncomingMessage buffer);
}