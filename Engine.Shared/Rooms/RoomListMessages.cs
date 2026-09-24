using System.Collections.Generic;
using Engine.Shared.Networking;

namespace Engine.Shared.Rooms;

/// <summary>
/// Client asks the server for the list of its active rooms.
/// </summary>
internal sealed partial class RoomListRequestMessage : NetMessage
{
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;
}

/// <summary>
/// Server answer with every active room that is not hidden.
/// </summary>
internal sealed partial class RoomListResponseMessage : NetMessage
{
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;

    public List<RoomInfo> Rooms { get; set; } = new();
}
