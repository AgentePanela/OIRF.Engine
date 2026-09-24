using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

[RegisterComponent("Transform"), NetworkedComponent]
public sealed partial class TransformComponent : Component
{
    [NetField] public Vector2 Position {get; set; } = Vector2.Zero;
    [NetField] public Vector2? Scale { get; set; }
    [NetField] public float Angle { get; set; } = 0f;
    [NetField] public bool Visible { get; set; } = true;
    [NetField] public EntityUid? Parent { get; set; }

    //public EntityUid MapId = EntityUid.Empty;
}
