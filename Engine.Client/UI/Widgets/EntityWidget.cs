using Engine.Client.Graphics;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;
using System.Linq;

namespace Engine.Client.UI.Widgets;

/// <summary>
/// A widget that previews an entity based on its <see cref="SpriteComponent"/>.
/// Updates dynamically based on the entity's components.
/// </summary>
public class EntityWidget : Panel
{
    private EntityUid _entityUid = EntityUid.Empty;

    public EntityWidget()
    {
    }

    public EntityWidget(EntityUid uid)
    {
        EntityUid = uid;
    }

    public EntityWidget(Shared.GameObjects.Entity entity)
    {
        EntityUid = entity.Uid;
    }

    /// <summary>
    /// The target entity's UID. Changing this value will completely rebuild the widget based on the entity Sprite Component
    /// </summary>
    public EntityUid EntityUid
    {
        get => _entityUid;
        set
        {
            _entityUid = value;
            RebuildWidgets();
        }
    }

    /// <summary>
    /// Updates the UID and rebuilds the widget rendering layers.
    /// </summary>
    public void SetEntity(EntityUid uid)
    {
        EntityUid = uid;
    }

    public override void InternalRender(RenderContext context)
    {
        UpdateState();
        base.InternalRender(context);
    }

    private void UpdateState()
    {
        if (_entityUid.Equals(EntityUid.Empty))
            return;

        var entityManager = IoCManager.Resolve<EntityManager>();
        
        if (!entityManager.HasEntity(_entityUid, out var ent) || ent.Deleting)
        {
            if (Widgets.Count > 0)
                Widgets.Clear();
            
            return;
        }

        if (!entityManager.TryComp<SpriteComponent>(_entityUid, out var spriteComp))
        {
            if (Widgets.Count > 0)
                Widgets.Clear();
            
            return;
        }
        int expectedWidgets = (string.IsNullOrEmpty(spriteComp.Key) ? 0 : 1) + 
                              (spriteComp.Layers?.Count(l => l.Visible && !string.IsNullOrEmpty(l.Key)) ?? 0);

        if (Widgets.Count != expectedWidgets)
        {
            RebuildWidgets();
            return; // state will be updated in the next render frame or inside RebuildWidgets.
        }

        float rotation = 0f;
        if (entityManager.TryComp<TransformComponent>(_entityUid, out var transform))
        {
            rotation = transform.Angle;
        }

        int wIdx = 0;

        if (!string.IsNullOrEmpty(spriteComp.Key))
        {
            var widget = (SpriteWidget)Widgets[wIdx++];
            widget.SpriteKey = spriteComp.Key;
            widget.Color = spriteComp.Color;
            widget.Rotation = rotation;
        }

        if (spriteComp.Layers != null)
        {
            foreach (var layer in spriteComp.Layers)
            {
                if (!layer.Visible || string.IsNullOrEmpty(layer.Key))
                    continue;

                var widget = (SpriteWidget)Widgets[wIdx++];
                widget.SpriteKey = layer.Key;
                widget.Color = layer.Color;
                widget.Rotation = rotation;
            }
        }
    }

    private void RebuildWidgets()
    {
        Widgets.Clear();

        if (_entityUid.Equals(EntityUid.Empty))
            return;

        var entityManager = IoCManager.Resolve<EntityManager>();
        if (!entityManager.HasEntity(_entityUid, out var ent) || ent.Deleting)
            return;

        if (!entityManager.TryComp<SpriteComponent>(_entityUid, out var spriteComp))
            return;

        float rotation = 0f;
        if (entityManager.TryComp<TransformComponent>(_entityUid, out var transform))
            rotation = transform.Angle;

        // base layer
        if (!string.IsNullOrEmpty(spriteComp.Key))
        {
            var baseWidget = new SpriteWidget(spriteComp.Key);
            baseWidget.Color = spriteComp.Color;
            baseWidget.Rotation = rotation;
            Widgets.Add(baseWidget);
        }

        // additional layers
        if (spriteComp.Layers != null && spriteComp.Layers.Count > 0)
        {
            foreach (var layer in spriteComp.Layers)
            {
                if (!layer.Visible || string.IsNullOrEmpty(layer.Key))
                    continue;

                var layerWidget = new SpriteWidget(layer.Key);
                layerWidget.Color = layer.Color;
                layerWidget.Rotation = rotation;
                Widgets.Add(layerWidget);
            }
        }
    }
}
