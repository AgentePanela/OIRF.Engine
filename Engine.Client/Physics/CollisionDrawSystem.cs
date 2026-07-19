using Engine.Client.Graphics;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.Physics.Configuration;
using Engine.Shared.Physics.Fixtures;
using Microsoft.Xna.Framework;

namespace Engine.Client.Physics;

public sealed class CollisionDrawSystem : EntityDrawSystem
{
    [Dependency] private readonly RenderManager _renderer = default!;
    [Dependency] private readonly Camera2D _camera = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly CollisionSystem _colSys = default!;
    
    private bool showMask = false;

    public override void Init()
    {
        base.Init();
        _cfg.Subs(PhysicsCvars.CollisionMask, value => showMask = value, true);
    }

    public override void Draw(float dt)
    {
        base.Draw(dt);
        if (!showMask)
            return;
            
        foreach (var (_, transform, col) in _colSys._entityBuffer)
        {
            if (!_camera.IsOnScreen(transform.Position))
                    continue;

            foreach (var (_, fixture) in col.Fixtures)
                DrawShape(fixture.Shape, transform.Position);
            
        }
    }

    private void DrawShape(CollisionShape shape, Vector2 worldPos)
    {
        switch (shape)
        {
            case BoxShape b:
                _renderer.DrawRect(b.GetBounds(worldPos), new Color(Color.Lime, 60));
                break;

            case CircleShape c:
                _renderer.DrawCircle(c.GetCenter(worldPos), c.Radius, new Color(Color.Cyan, 60));
                break;
            
            case PolygonShape p:
                _renderer.DrawPolygon(p.GetWorldVertices(worldPos), new Color(Color.Yellow, 60));
                break;
        }
    }
}