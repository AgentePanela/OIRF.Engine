using System.Collections.Generic;
using Engine.Shared.GameObjects;

namespace Engine.Shared.Tags;

[RegisterComponent("Tag"), NetworkedComponent]
public sealed partial class TagComponent : Component
{
    [NetField]
    public HashSet<ProtoId<TagPrototype>> Tags = new();
}