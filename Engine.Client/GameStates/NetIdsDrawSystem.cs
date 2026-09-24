using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.GameStates;

/// <summary>
/// Writes the net id, the tick of the last state and the block it came from over every replicated entity. Toggled by
/// the netids command. 
/// </summary>
public sealed class NetIdsDrawSystem : EntityDrawSystem
{
    private static readonly Color LabelColor = new(255, 220, 120);
    private static readonly Color StaleColor = new(255, 120, 120);

    /// <summary>
    /// How many ticks without a state before the label turns red.
    /// </summary>
    private const uint StaleAfter = 30;

    [Dependency] private readonly RenderManager _renderer = default!;
    [Dependency] private readonly Camera2D _camera = default!;
    [Dependency] private readonly IFontManager _fonts = default!;
    [Dependency] private readonly ClientGameStateMetrics _metrics = default!;

    public bool Enabled { get; set; }

    public override void Draw(float dt)
    {
        base.Draw(dt);

        if (!Enabled)
            return;

        var newest = _metrics.LastAppliedTick;

        foreach (var (uid, transform) in GetEntitiesWithComp<TransformComponent>())
        {
            if (_entManager.GetEntity(uid) is not { } ent || !ent.NetId.IsValid)
                continue;

            if (!_camera.IsOnScreen(transform.Position))
                continue;

            var text = $"n{ent.NetId.Id}";
            var color = LabelColor;

            if (_metrics.Entities.TryGetValue(ent.NetId, out var traffic))
            {
                text += $" @{traffic.LastUpdate} {(traffic.Origin == EntityBlockKind.Global ? "G" : "R")}";

                if (newest.Value - traffic.LastUpdate.Value > StaleAfter)
                    color = StaleColor;
            }

            _renderer.Submit(new RenderQueue(new Label2D(_fonts.Get(12f), text) { Color = color },
                transform.Position, unshaded: true));
        }
    }
}
