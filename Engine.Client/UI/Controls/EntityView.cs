using Apos.Shapes;
using Engine.Client.Assets;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Engine.Client.UI;

/// <summary>
/// Previews a live entity inside UI.
/// </summary>
public partial class EntityView : Control
{
    private EntityUid _entityUid = EntityUid.Empty;

    /// <summary>
    /// The entity to preview.
    /// </summary>
    public EntityUid EntityUid
    {
        get => _entityUid;
        set => SetLayoutField(ref _entityUid, value);
    }

    /// <summary>
    /// Stretches the base sprite to fill Bounds instead of native size.
    /// </summary>
    [StyleField("stretch", false)]
    private bool? _stretch;

    /// <summary>
    /// Multiplies on every layers own color.
    /// </summary>
    [StyleField("tint", 0xFFFFFFFFu)]
    private Color? _tint;

    /// <summary>
    /// Rotation in radians, used unless FollowEntityAngle is set.
    /// </summary>
    public float Rotation { get; set; }

    /// <summary>
    /// Draws at the entity own TransformComponent.Angle instead of forced Rotation.
    /// </summary>
    public bool FollowEntityAngle { get; set; }

    private readonly List<TextureRect> _icons = new();

    public EntityView()
    {
    }

    public EntityView(EntityUid uid) : this()
    {
        EntityUid = uid;
    }

    public void Refresh() => InvalidateLayout();

    protected override void OnDispose() => _entityUid = EntityUid.Empty;

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        if (!TryGetBaseRegion(out var region))
            return Vector2.Zero;

        return new Vector2(region.Width, region.Height);
    }

    private bool TryGetBaseRegion(out Rectangle region)
    {
        region = default;

        var entMan = GameClient.EntityManager;
        if (EntityUid == EntityUid.Empty
            || !entMan.HasEntity(EntityUid, out var ent) || ent.Deleting
            || !entMan.TryComp<SpriteComponent>(EntityUid, out var sprite)
            || string.IsNullOrEmpty(sprite.Key))
        {
            return false;
        }

        if (!IoCManager.Resolve<IAssetManager>().GetTexture(sprite.Key, out var atlasSprite, out _))
            return false;

        region = atlasSprite.Region;
        return true;
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        var used = 0;
        var entMan = GameClient.EntityManager;

        if (EntityUid != EntityUid.Empty
            && entMan.HasEntity(EntityUid, out var ent) && !ent.Deleting
            && entMan.TryComp<SpriteComponent>(EntityUid, out var sprite)
            && !string.IsNullOrEmpty(sprite.Key))
        {
            var rotation = Rotation;
            if (FollowEntityAngle && entMan.TryComp<TransformComponent>(EntityUid, out var transform))
                rotation = transform.Angle;

            var scale = Vector2.One;
            if (Stretch
                && IoCManager.Resolve<IAssetManager>().GetTexture(sprite.Key, out var baseAtlas, out _)
                && baseAtlas.Region is { Width: > 0, Height: > 0 } baseRegion)
            {
                scale = new Vector2(Bounds.Width / (float)baseRegion.Width, Bounds.Height / (float)baseRegion.Height);
            }

            var anchor = new Vector2(Bounds.X + Bounds.Width / 2f, Bounds.Y + Bounds.Height / 2f);

            used += ArrangeIcon(used, sprite.Key, sprite.Color, sprite.Origin, Vector2.Zero,
                anchor, scale, rotation, sprite.Effects);

            if (sprite.LayersDirty)
            {
                sprite.Layers.Sort((a, b) => a.Order.CompareTo(b.Order));
                sprite.LayersDirty = false;
            }

            foreach (var layer in sprite.Layers)
            {
                if (!layer.Visible || string.IsNullOrEmpty(layer.Key))
                    continue;

                used += ArrangeIcon(used, layer.Key, layer.Color, layer.Origin, layer.Offset,
                    anchor, scale, rotation, sprite.Effects);
            }
        }

        for (var i = used; i < _icons.Count; i++)
            _icons[i].Visible = false;
    }

    private int ArrangeIcon(int index, string key, Color color, Vector2? origin, Vector2 nativeOffset,
        Vector2 anchor, Vector2 scale, float rotation, SpriteEffects effects)
    {
        if (!IoCManager.Resolve<IAssetManager>().GetTexture(key, out var atlasSprite, out _))
            return 0;

        var region = atlasSprite.Region;
        var drawSize = new Vector2(region.Width, region.Height) * scale;

        var offset = nativeOffset * scale;
        if (offset != Vector2.Zero)
            offset = Vector2.Transform(offset, Matrix.CreateRotationZ(rotation));

        var center = anchor + offset;
        var topLeft = center - drawSize / 2f;

        var icon = GetIcon(index);
        icon.Visible = true;
        icon.Key = key;
        icon.Tint = Multiply(color, Tint);
        icon.Rotation = rotation;
        icon.Origin = origin;
        icon.Effects = effects;
        icon.Stretch = true;
        icon.Arrange(new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)drawSize.X, (int)drawSize.Y));

        return 1;
    }

    public TextureRect GetIcon(int index)
    {
        while (_icons.Count <= index)
        {
            var icon = new TextureRect();
            _icons.Add(icon);
            AddChild(icon);
        }

        return _icons[index];
    }

    private static Color Multiply(Color a, Color b) => new(a.ToVector4() * b.ToVector4());
}
