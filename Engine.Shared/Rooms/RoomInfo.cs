using System;

namespace Engine.Shared.Rooms;

/// <summary>
/// Snapshot of a server room, as shown in the client rooms list.
/// </summary>
[Serializable]
public sealed class RoomInfo
{
    /// <summary>
    /// The identifier used to join the room.
    /// </summary>
    public string RoomId = "";

    /// <summary>
    /// The room class name, useful to filter what kind of room it is.
    /// </summary>
    public string RoomType = "";

    /// <summary>
    /// How many sessions are inside right now.
    /// </summary>
    public int Sessions;

    /// <summary>
    /// null = no limit.
    /// </summary>
    public uint? MaxSessions;

    public bool Locked;
}
