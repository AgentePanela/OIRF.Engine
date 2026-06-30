using System.Collections.Generic;
using Engine.Shared.GameObjects;

namespace Engine.Shared.Tags;

[RegisterComponent("Tag")]
public sealed class TagComponent : Component
{
    public HashSet<ProtoId<TagPrototype>> Tags = new();
}