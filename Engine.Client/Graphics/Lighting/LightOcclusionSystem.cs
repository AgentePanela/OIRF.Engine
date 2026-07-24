using Engine.Client.Assets;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Components.Lighting;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Resolves the world-space AABB of occluders for the lighting system.
/// </summary>
public sealed class LightOcclusionSystem : EntitySystem
{
    [Dependency] private readonly IAssetManager _assetMan = default!;

    public LightOcclusionSystem()
    {
        IoCManager.ResolveDependencies(this);
    }

    /// <summary>
    /// AABB for an occluder. Rectangle and Circle come from the shape
    /// parameters; Sprite uses the resolved sprite region, with a 32x32
    /// fallback when nothing can be resolved.
    /// </summary>
    public Rectangle GetOccluderBounds(
        EntityUid uid,
        OccluderComponent occluder,
        TransformComponent transform,
        EntityManager? entMan = null)
    {
        var center = transform.Position + occluder.Offset;

        switch (occluder.Shape)
        {
            case OccluderShape.Rectangle:
                return new Rectangle(
                    (int)(center.X - occluder.Size.X / 2f),
                    (int)(center.Y - occluder.Size.Y / 2f),
                    (int)occluder.Size.X,
                    (int)occluder.Size.Y);

            case OccluderShape.Circle:
                return new Rectangle(
                    (int)(center.X - occluder.Radius),
                    (int)(center.Y - occluder.Radius),
                    (int)(occluder.Radius * 2f),
                    (int)(occluder.Radius * 2f));

            case OccluderShape.Sprite:
                return SpriteBounds(uid, occluder, center, entMan);

            default:
                return new Rectangle((int)center.X, (int)center.Y, 0, 0);
        }
    }

    private Rectangle SpriteBounds(
        EntityUid uid,
        OccluderComponent occluder,
        Vector2 center,
        EntityManager? entMan)
    {
        // the atlas region is 1:1 with world size for regular sprites
        const int Fallback = 32;
        if (entMan is null) return SizedBox(center, Fallback, Fallback);

        if (entMan.TryComp<SpriteComponent>(uid, out var spriteComp))
        {
            if (spriteComp.Spr is { CachedRegion: var region } && region.Width > 0 && region.Height > 0)
                return SizedBox(center, region.Width, region.Height);

            if (!string.IsNullOrEmpty(spriteComp.Key) &&
                _assetMan.GetTexture(spriteComp.Key, out var atlasSpr, out _))
            {
                return SizedBox(center, atlasSpr.Region.Width, atlasSpr.Region.Height);
            }
        }

        return SizedBox(center, Fallback, Fallback);
    }

    private static Rectangle SizedBox(Vector2 center, int w, int h) =>
        new(
            (int)(center.X - w / 2f),
            (int)(center.Y - h / 2f),
            w,
            h);
}
