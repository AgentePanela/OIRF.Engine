using Engine.Client.Graphics.Fonts;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Components.Lighting;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Debug overlay for OccluderComponent masks, toggled by <see cref="LightingCvars.ShowOccluderMask"/>.
/// Rectangle/Circle occluders draw their actual shadow-casting shape; Sprite occluders
/// have no fixed mask (just a bounding-box guess off the sprite region) so they get a
/// "sprite mask" label instead.
/// </summary>
public sealed class OccluderDebugDrawSystem : EntityDrawSystem
{
    [Dependency] private readonly RenderManager _renderer = default!;
    [Dependency] private readonly Camera2D _camera = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly LightOcclusionSystem _occlusionSys = default!;

    private bool _showMask;

    public override void Init()
    {
        base.Init();
        _cfg.Subs(LightingCvars.ShowOccluderMask, v => _showMask = v, true);
    }

    public override void Draw(float dt)
    {
        base.Draw(dt);
        if (!_showMask)
            return;

        foreach (var (uid, occluder, transform) in GetEntitiesWithComp<OccluderComponent, TransformComponent>())
        {
            if (!_camera.IsOnScreen(transform.Position))
                continue;

            switch (occluder.Shape)
            {
                case OccluderShape.Rectangle:
                    var bounds = _occlusionSys.GetOccluderBounds(uid, occluder, transform, _entManager);
                    _renderer.DrawRect(bounds, new Color(Color.Lime, 60), unshaded: true);
                    break;

                case OccluderShape.Circle:
                    _renderer.DrawCircle(transform.Position + occluder.Offset, occluder.Radius, new Color(Color.Lime, 60), unshaded: true);
                    break;

                case OccluderShape.Sprite:
                    // no fixed mask to outline - it's a bounding-box guess off the sprite region
                    _renderer.DrawString(new Label2D(TextStyle.Debug, "sprite mask"), transform.Position + occluder.Offset);
                    break;
            }
        }
    }
}
