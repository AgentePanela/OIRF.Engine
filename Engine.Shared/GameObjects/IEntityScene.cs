using System;
using System.Collections.Concurrent;

namespace Engine.Shared.GameObjects;

public interface IEntityScene
{
    public ConcurrentDictionary<EntityUid, Entity> Entities { get; }
    public int EntUidIndex { get; set; }
    public ConcurrentDictionary<Type, ConcurrentDictionary<EntityUid, Component>> Components { get; }
}
