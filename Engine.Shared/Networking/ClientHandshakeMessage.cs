using Lidgren.Network;

namespace Engine.Shared.Networking;

/// <summary>
/// Sent by the server to a client right after its <see cref="NetSession"/> is created.
/// </summary>
public sealed class ClientHandshakeMessage : INetMessage
{
    public string SessionId { get; private set; } = "";

    public ClientHandshakeMessage() { }

    public ClientHandshakeMessage(string sessionId)
    {
        SessionId = sessionId;
    }

    public void WriteToBuffer(NetOutgoingMessage buffer) => buffer.Write(SessionId);
    public void ReadFromBuffer(NetIncomingMessage buffer) => SessionId = buffer.ReadString();
}
