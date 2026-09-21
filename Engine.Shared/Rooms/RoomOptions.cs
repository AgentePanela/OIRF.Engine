using System;

namespace Engine.Shared.Rooms;

/// <summary>
/// Base for the data a session sends when joining a room.
/// The subclass MUST be marked with <see cref="SerializableAttribute"/>!
/// </summary>
[Serializable]
public abstract class RoomOptions
{
}

/// <summary>
/// Options for rooms that don't need any.
/// </summary>
[Serializable]
public sealed class EmptyRoomOptions : RoomOptions
{
}
