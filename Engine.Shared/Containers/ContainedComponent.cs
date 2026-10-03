using Engine.Shared.GameObjects;

namespace Engine.Shared.Containers;

/// <summary>
/// Marks an entity as being inside a container.
/// </summary>
[RegisterComponent("Contained"), NetworkedComponent]
public sealed partial class ContainedComponent : Component
{
    /// <summary>
    /// The entity whose <see cref="ContainerComponent"/> holds this one.
    /// </summary>
    [NetField, AutoDirty] public partial EntityUid ContainerOwner { get; set; }

    [NetField, AutoDirty] public partial string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Position in the owner container
    /// </summary>
    [NetField, AutoDirty] public partial int Order { get; set; }
}
