using System.Collections.Generic;
using Engine.Shared.GameObjects;

namespace Engine.Shared.Containers;

/// <summary>
/// Lets an entity hold other entities in named containers.
/// </summary>
[RegisterComponent("Container"), NetworkedComponent]
public sealed partial class ContainerComponent : Component
{
    /// <summary>
    /// Declared containers, id to kind. Change it through <see cref="ContainerSystem.EnsureContainer{T}"/> and
    /// <see cref="ContainerSystem.RemoveContainer"/>, or it will not replicate!!!!
    /// </summary>
    [NetField]
    public Dictionary<string, ContainerKind> Containers { get; set; } = new();

    internal Dictionary<string, BaseContainer> Instances { get; } = new();
}
