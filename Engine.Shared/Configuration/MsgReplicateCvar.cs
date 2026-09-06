using Lidgren.Network;
using Engine.Shared.Networking;

namespace Engine.Shared.Configuration;

/// <summary>
/// Sent by the server to sync replicated cvars.
/// </summary>
public sealed class MsgReplicateCvar : INetMessage
{
    public string Name { get; private set; } = "";
    public string Value { get; private set; } = "";

    public MsgReplicateCvar() { }

    public MsgReplicateCvar(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public void WriteToBuffer(NetOutgoingMessage buffer)
    {
        buffer.Write(Name);
        buffer.Write(Value);
    }

    public void ReadFromBuffer(NetIncomingMessage buffer)
    {
        Name = buffer.ReadString();
        Value = buffer.ReadString();
    }
}
