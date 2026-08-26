using System;
using Engine.Client.Assets;
using Engine.Shared.GameObjects;
using Engine.Shared.Lighting;
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
    /// Conservative broad-phase test: can this occluder possibly overlap
    /// <paramref name="bounds"/>? Rectangle and Circle answer straight from the
    /// component; a Sprite occluder reuses the extent resolved on an earlier
    /// frame, and returns true when it has none yet so
    /// <see cref="GetOccluderBounds"/> can resolve it properly. Never rejects
    /// an occluder that would have passed the exact test.
    /// </summary>
    public bool MayOverlap(OccluderComponent occluder, TransformComponent transform, Rectangle bounds)
    {
        var center = transform.Position + occluder.Offset;

        float halfW, halfH;
        switch (occluder.Shape)
        {
            case OccluderShape.Rectangle:
                halfW = occluder.Size.X * 0.5f;
                halfH = occluder.Size.Y * 0.5f;
                break;

            case OccluderShape.Circle:
                halfW = halfH = occluder.Radius;
                break;

            default:
                if (occluder.CachedSpriteHalfExtent <= 0f)
                    return true;
                halfW = halfH = occluder.CachedSpriteHalfExtent;
                break;
        }

        // 1px slack: the exact bounds truncate to int, which can nudge the box
        // outward by up to a pixel. Cheaper to be slightly generous here than
        // to have a wall flicker at the edge of the padded view
        const float Slack = 1f;

        return center.X + halfW + Slack > bounds.Left
            && center.X - halfW - Slack < bounds.Right
            && center.Y + halfH + Slack > bounds.Top
            && center.Y - halfH - Slack < bounds.Bottom;
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
                return CacheExtent(occluder, center, region.Width, region.Height);

            if (!string.IsNullOrEmpty(spriteComp.Key) &&
                _assetMan.GetTexture(spriteComp.Key, out var atlasSpr, out _))
            {
                return CacheExtent(occluder, center, atlasSpr.Region.Width, atlasSpr.Region.Height);
            }
        }

        // the fallback isn't a real measurement (assets may still be loading),
        // so don't cache it - a too-small extent would reject the occluder
        // forever and it'd never get resolved again
        return SizedBox(center, Fallback, Fallback);
    }

    // grow-only, so a sprite that shrinks leaves an over-estimate behind
    // rather than a broad-phase reject it shouldn't have made
    private static Rectangle CacheExtent(OccluderComponent occluder, Vector2 center, int w, int h)
    {
        float half = MathF.Max(w, h) * 0.5f;
        if (half > occluder.CachedSpriteHalfExtent)
            occluder.CachedSpriteHalfExtent = half;

        return SizedBox(center, w, h);
    }

    private static Rectangle SizedBox(Vector2 center, int w, int h) =>
        new(
            (int)(center.X - w / 2f),
            (int)(center.Y - h / 2f),
            w,
            h);
}
