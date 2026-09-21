using System.Collections.Generic;
using Engine.Server.Rooms;
using Engine.Shared.GameObjects;

namespace Engine.Server;

/// <summary>
/// Server-side implementation of IEntityScene.
/// </summary>
public abstract class EntityRoom : Room, IEntityScene
{
    public HashSet<EntityUid> OwnedEntities { get; } = new();
}
