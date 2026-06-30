using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

[RegisterComponent("Transform")]
public sealed class TransformComponent : Component
{
    public Vector2 Position {get; set; } = Vector2.Zero;
    public Vector2? Scale { get; set; }
    public float Angle { get; set; } = 0f;
    public bool Visible { get; set; } = true;


    //public EntityUid MapId = EntityUid.Empty;
}
