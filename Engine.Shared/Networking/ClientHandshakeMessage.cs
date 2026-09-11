namespace Engine.Shared.Networking;

/// <summary>
/// Sent by the server to a client right after its <see cref="NetSession"/> is created.
/// </summary>
public sealed partial class ClientHandshakeMessage : NetMessage
{
    public string SessionId { get; private set; } = "";

    public ClientHandshakeMessage(string sessionId)
    {
        SessionId = sessionId;
    }
}
