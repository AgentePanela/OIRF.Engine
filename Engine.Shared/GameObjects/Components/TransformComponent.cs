using System;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

[RegisterComponent("Transform"), NetworkedComponent]
public sealed partial class TransformComponent : Component
{
    // todo: remove (or make obsolet) the component logic from here and pass everything to transform system

    /// <summary>
    /// Position relative to <see cref="Parent"/>. The world position when there is no parent.
    /// </summary>
    [NetField, AutoDirty, Ignore(IgnoreIn.Serialization)] public partial Vector2 LocalPosition { get; set; } = Vector2.Zero;

    /// <summary>
    /// Angle added to the <see cref="Parent"/> world angle. The world angle when there is no parent.
    /// </summary>
    [NetField, AutoDirty, Ignore(IgnoreIn.Serialization)] public partial float LocalAngle { get; set; } = 0f;

    [NetField, AutoDirty] public partial Vector2? Scale { get; set; }
    [NetField, AutoDirty] public partial bool Visible { get; set; } = true;

    [Ignore(IgnoreIn.Clone)]
    public Vector2 Position
    {
        get => TryGetParent(out var parent)
            ? parent.Position + Rotate(LocalPosition, parent.Angle)
            : LocalPosition;
        set => LocalPosition = TryGetParent(out var parent)
            ? Rotate(value - parent.Position, -parent.Angle)
            : value;
    }

    [Ignore(IgnoreIn.Clone)]
    public float Angle
    {
        get => TryGetParent(out var parent) ? parent.Angle + LocalAngle : LocalAngle;
        set => LocalAngle = TryGetParent(out var parent) ? value - parent.Angle : value;
    }

    [NetField]
    public EntityUid? Parent
    {
        get;
        set
        {
            if (field == value)
                return;

            var transformSys = IoCManager.Resolve<EntityManager>().GetSystem<TransformSystem>();

            if (value is { } newParent && transformSys is not null && transformSys.WouldCycle(Owner, newParent))
            {
                Log.Error($"Refused to parent {Owner} to {newParent}: it would make a cycle.");
                return;
            }

            var old = field;
            field = value;

            DirtyField(NetFields.Parent);
            transformSys?.ReparentChild(Owner, old, value);
        }
    }

    private bool TryGetParent(out TransformComponent parent)
    {
        if (Parent is { } uid && EntityManager.Instance is { } entMan && entMan.TryComp(uid, out parent!))
            return true;

        parent = null!;
        return false;
    }

    internal static Vector2 Rotate(Vector2 v, float angle)
    {
        if (angle == 0f)
            return v;

        var (sin, cos) = MathF.SinCos(angle);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    //public EntityUid MapId = EntityUid.Empty;
}
