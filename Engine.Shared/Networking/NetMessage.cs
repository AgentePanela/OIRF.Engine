using Lidgren.Network;

namespace Engine.Shared.Networking;

public interface INetMessage
{
    public abstract void WriteToBuffer(NetOutgoingMessage buffer);
    public abstract void ReadFromBuffer(NetIncomingMessage buffer);
}