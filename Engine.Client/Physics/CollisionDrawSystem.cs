using Engine.Client.Graphics;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.Physics.Fixtures;
using Microsoft.Xna.Framework;

namespace Engine.Client.Physics;

public sealed class CollisionDrawSystem : EntityDrawSystem
{
    [Dependency] private readonly IRenderManager _renderer = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly CollisionSystem _colSys = default!;
    
    private bool showMask = false;

    public override void Init()
    {
        base.Init();
        _cfg.Subs(PhysicsCvars.CollisionMask, value => showMask = value, true);
    }

    public override void Draw(in DrawContext ctx)
    {
        base.Draw(ctx);
        if (!showMask)
            return;
            
        foreach (var (_, _, transform, col) in _colSys._entityBuffer)
        {
            if (!ctx.Camera.IsOnScreen(transform.Position))
                    continue;

            foreach (var (_, fixture) in col.Fixtures)
                DrawShape(fixture.Shape, transform.Position, fill: MaskColor(fixture.Shape));

        }
    }

    private static Color MaskColor(CollisionShape shape) => shape switch
    {
        BoxShape => new Color(Color.Lime, 60),
        CircleShape => new Color(Color.Cyan, 60),
        _ => new Color(Color.Yellow, 60),
    };

    /// <summary>
    /// Draws every fixture of <paramref name="uid"/>, whatever its shape.
    /// </summary>
    /// <returns>False when it has nothing to draw.</returns>
    public bool DrawFixtures(EntityUid uid, Color? fill = null, Color? border = null, float thickness = 1f, bool unshaded = true)
    {
        if (!TryTransform(uid, out var transform) || !TryComp<CollisionComponent>(uid, out var col)
            || col.Fixtures.Count == 0)
            return false;

        foreach (var (_, fixture) in col.Fixtures)
            DrawShape(fixture.Shape, transform.Position, fill, border, thickness);

        return true;
    }

    /// <summary>
    /// Draws one collision shape sitting at <paramref name="worldPos"/>.
    /// </summary>
    public void DrawShape(CollisionShape shape, Vector2 worldPos, Color? fill = null, Color? border = null,
        float thickness = 1f, bool unshaded = true)
    {
        // the renderer only draws a border that has a thickness
        var borderThickness = border is null ? 0f : thickness;

        switch (shape)
        {
            case BoxShape b:
                _renderer.DrawRect(b.GetBounds(worldPos), fill, border, borderThickness, unshaded: unshaded);
                break;

            case CircleShape c:
                _renderer.DrawCircle(c.GetCenter(worldPos), c.Radius, fill, border, borderThickness, unshaded: unshaded);
                break;

            case PolygonShape p:
                _renderer.DrawPolygon(p.GetWorldVertices(worldPos), border ?? fill, thickness, unshaded: unshaded);
                break;
        }
    }
}