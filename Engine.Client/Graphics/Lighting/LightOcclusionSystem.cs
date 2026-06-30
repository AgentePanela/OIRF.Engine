using Engine.Client.Assets;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Components.Lighting;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Resolves the world-space AABB of <see cref="OccluderComponent"/> instances
/// for the lighting system. Kept as an EntitySystem so it shares the standard
/// system lifecycle (Init/Update/Draw), but currently no per-frame work is
/// required — the AABB calculation is pure data and is exposed as a
/// method that <see cref="LightingSystem"/> calls directly while iterating
/// its own entity query.
/// </summary>
public sealed class LightOcclusionSystem : EntitySystem
{
    [Dependency] private readonly IAssetManager _assetMan = default!;

    public LightOcclusionSystem()
    {
        IoCManager.ResolveDependencies(this);
    }

    /// <summary>
    /// Get the world-space AABB for an occluder. For <see cref="OccluderShape.Rectangle"/>
    /// and <see cref="OccluderShape.Circle"/> this is computed from the shape
    /// parameters. For <see cref="OccluderShape.Sprite"/> the entity's
    /// <see cref="SpriteComponent"/> is queried for the resolved atlas
    /// region; a 32×32 fallback is used when no sprite can be resolved.
    /// </summary>
    public Rectangle GetOccluderBounds(
        EntityUid uid,
        OccluderComponent occluder,
        TransformComponent transform,
        EntityManager? entMan = null)
    {
        switch (occluder.Shape)
        {
            case OccluderShape.Rectangle:
                return new Rectangle(
                    (int)(transform.Position.X - occluder.Size.X / 2f),
                    (int)(transform.Position.Y - occluder.Size.Y / 2f),
                    (int)occluder.Size.X,
                    (int)occluder.Size.Y);

            case OccluderShape.Circle:
                return new Rectangle(
                    (int)(transform.Position.X - occluder.Radius),
                    (int)(transform.Position.Y - occluder.Radius),
                    (int)(occluder.Radius * 2f),
                    (int)(occluder.Radius * 2f));

            case OccluderShape.Sprite:
                return SpriteBounds(uid, occluder, transform, entMan);

            default:
                return new Rectangle((int)transform.Position.X, (int)transform.Position.Y, 0, 0);
        }
    }

    private Rectangle SpriteBounds(
        EntityUid uid,
        OccluderComponent occluder,
        TransformComponent transform,
        EntityManager? entMan)
    {
        // The CachedRegion on the sprite is the atlas-space bounding box of
        // the chosen tile — the physical world size is the region's
        // width/height (the atlas is 1:1 with the world for non-tilemap
        // sprites). Fall back to 32×32 if nothing is resolved, so we never
        // return a zero-size box (which would be silently ignored by
        // ShadowGeometry).
        const int Fallback = 32;
        if (entMan is null) return SizedBox(transform, Fallback, Fallback);

        if (entMan.TryComp<SpriteComponent>(uid, out var spriteComp))
        {
            if (spriteComp.Spr is { CachedRegion: var region } && region.Width > 0 && region.Height > 0)
                return SizedBox(transform, region.Width, region.Height);

            if (!string.IsNullOrEmpty(spriteComp.Key) &&
                _assetMan.GetTexture(spriteComp.Key, out var atlasSpr, out _))
            {
                return SizedBox(transform, atlasSpr.Region.Width, atlasSpr.Region.Height);
            }
        }

        return SizedBox(transform, Fallback, Fallback);
    }

    private static Rectangle SizedBox(TransformComponent transform, int w, int h) =>
        new(
            (int)(transform.Position.X - w / 2f),
            (int)(transform.Position.Y - h / 2f),
            w,
            h);
}
