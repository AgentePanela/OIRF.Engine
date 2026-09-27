using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

[RegisterComponent("Transform"), NetworkedComponent, AutoDirty]
public sealed partial class TransformComponent : Component
{
    [NetField] public partial Vector2 Position {get; set; } = Vector2.Zero;
    [NetField] public partial Vector2? Scale { get; set; }
    [NetField] public partial float Angle { get; set; } = 0f;
    [NetField] public partial bool Visible { get; set; } = true;
    [NetField] public partial EntityUid? Parent { get; set; }

    //public EntityUid MapId = EntityUid.Empty;
}
