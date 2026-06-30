using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;

namespace Engine.Server;

/// <summary>
/// Server-side implementation of IEntityScene.
/// A flat entity container without any rendering or MonoGame scene system.
/// The server uses a single scene for the entire game world.
/// </summary>
internal sealed class EntityRoom : IEntityScene
{
    public Dictionary<EntityUid, Entity> Entities { get; } = new();
    public int EntUidIndex { get; set; } = 1;
    public Dictionary<Type, Dictionary<EntityUid, Component>> Components { get; } = new();
}
