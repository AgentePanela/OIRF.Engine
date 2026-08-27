using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Engine.Shared.GameObjects;

public interface IEntityScene
{
    public ConcurrentDictionary<EntityUid, Entity> Entities { get; }
    public int EntUidIndex { get; set; }
    public ConcurrentDictionary<Type, Dictionary<EntityUid, Component>> Components { get; }
}
