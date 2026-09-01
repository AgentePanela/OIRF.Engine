using Lidgren.Network;

namespace Engine.Shared.Networking;

public interface INetMessage
{
    public void WriteToBuffer(NetOutgoingMessage buffer);
    public void ReadFromBuffer(NetIncomingMessage buffer);
}