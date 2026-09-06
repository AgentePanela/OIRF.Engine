using Lidgren.Network;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Networking;

public interface INetMessage
{
    public void WriteToBuffer(NetOutgoingMessage buffer);
    public void ReadFromBuffer(NetIncomingMessage buffer);
}

public sealed partial class ClientHandshakeMessage2 : INetMessage
{
    public EntityUid Foo { get; set; }

    public void Foo2()
    {
        WriteToBuffer();
    }
}
