using System.Collections.Generic;
using Engine.Shared.GameObjects;

namespace Engine.Server;

/// <summary>
/// Server-side implementation of IEntityScene.
/// </summary>
internal sealed class EntityRoom : IEntityScene
{
    public HashSet<EntityUid> OwnedEntities { get; } = new();
}
