using Engine.Shared.Networking;

namespace Engine.Shared.Rooms;


internal sealed partial class JoinRoomMessage : NetMessage
{
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;

    public string RoomId { get; set; } = "";

    /// <summary>
    /// Null is treated as <see cref="EmptyRoomOptions"/>.
    /// </summary>
    public RoomOptions? Options { get; set; }
}

internal sealed partial class LeaveRoomMessage : NetMessage
{
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;

    public string Reason { get; set; } = "";
}

internal sealed partial class RoomResonseMessage : NetMessage
{
    public ResponseType responseType { get; set; }
    public string RoomId { get; set; } = "";

    /// <summary>
    /// Used by leave or error.
    /// </summary>
    public string Reason { get; set; } = "";

    public enum ResponseType
    {
        Joined,
        Leaved,
        Error
    }
}
