using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

[RegisterComponent("Transform"), NetworkedComponent]
public sealed partial class TransformComponent : Component
{
    [NetField, AutoDirty] public partial Vector2 Position {get; set; } = Vector2.Zero;
    [NetField, AutoDirty] public partial Vector2? Scale { get; set; }
    [NetField, AutoDirty] public partial float Angle { get; set; } = 0f;
    [NetField, AutoDirty] public partial bool Visible { get; set; } = true;

    [NetField]
    public EntityUid? Parent
    {
        get;
        set
        {
            if (field == value)
                return;

            var old = field;
            field = value;

            DirtyField(NetFields.Parent);
            IoCManager.Resolve<EntityManager>().GetSystem<TransformSystem>()?.ReparentChild(Owner, old, value);
        }
    }

    //public EntityUid MapId = EntityUid.Empty;
}
