using System;
using System.Collections.Generic;

namespace Engine.Shared.GameObjects;

public interface IEntityScene
{
    public Dictionary<EntityUid, Entity> Entities { get; }
    public int EntUidIndex { get; set; }
    public Dictionary<Type, Dictionary<EntityUid, Component>> Components { get; }
}