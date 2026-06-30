using System;
using Engine.Client.Graphics;
using Engine.Shared.GameObjects;

namespace Engine.Client.GameObjects;

/// <summary>
/// Controls Camera2DComponent, does not have any public function at the moment.
/// </summary>
public sealed class Camera2DSystem : EntitySystem
{
    [Dependency] private readonly Camera2D _cam = default!;

    public override void Init()
    {
        base.Init();
        SubscribeEvent<Camera2DComponent, CompAddedEvent>(OnCompAdded);
    }

    private void OnCompAdded(EntityUid uid, Camera2DComponent comp, CompAddedEvent args)
    {
        if (!comp.Active || comp.InitialZoom is null)
            return;
        _cam.Zoom = comp.InitialZoom.Value;
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        var query = GetEntitiesWithComp<Camera2DComponent>();
        foreach ((var uid, var comp) in query)
        {
            if (!comp.Active || !TryTransform(uid, out var trans))
                continue;

            _cam.MaximumZoom = comp.MaximumZoom;
            _cam.MinimumZoom = comp.MinimumZoom;
            if (comp.UsePosition) _cam.LookAt(trans.Position);
            if (comp.UseAngle) _cam.Rotation = trans.Angle;
            return; // only the first entity with the camera active will set the camera attributes.
        }
    }
}
