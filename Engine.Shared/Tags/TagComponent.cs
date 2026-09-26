using System.Collections.Generic;
using Engine.Shared.GameObjects;

namespace Engine.Shared.Tags;

[RegisterComponent("Tag"), NetworkedComponent]
public sealed partial class TagComponent : Component
{
    /// <summary>
    /// Use TagSystem to set this component tags! Setting direct from the component
    /// will not replicate to the clients!!!
    /// </summary>
    [NetField]
    public partial HashSet<ProtoId<TagPrototype>> Tags { get; set; } = new();
}