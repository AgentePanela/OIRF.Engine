using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

public sealed class TransformSystem : EntitySystem
{
    /// <summary>
    /// Returns the closest entity from position within <paramref name="hitRadius"/> units.
    /// </summary>
    /// <returns><see cref="EntityUid.Empty"/> if none found.</returns>
    public EntityUid GetEntityAtWorld(Vector2 worldPos, float hitRadius = 2f, bool requireVisible = true)
    {
        TryGetEntityAtWorld(worldPos, out var uid, hitRadius, requireVisible);
        return uid;
    }

    /// <summary>
    /// Tries to find the closest entity from the position within <paramref name="hitRadius"/> units.
    /// </summary>
    public bool TryGetEntityAtWorld(
        Vector2 worldPos,
        out EntityUid uid,
        float hitRadius = 2f,
        bool requireVisible = true)
    {
        uid = EntityUid.Empty;

        float hitRadiusSq = hitRadius * hitRadius;
        float bestDistSq = float.MaxValue;

        foreach (var (entUid, transform) in GetEntitiesWithComp<TransformComponent>())
        {
            if (requireVisible && !transform.Visible)
                continue;

            float dx = transform.Position.X - worldPos.X;
            float dy = transform.Position.Y - worldPos.Y;
            float distSq = dx * dx + dy * dy;

            if (distSq > hitRadiusSq)
                continue;

            if (distSq >= bestDistSq)
                continue;

            bestDistSq = distSq;
            uid = entUid;
        }

        return uid != EntityUid.Empty;
    }

    /// <summary>
    /// Returns all entities whose position falls within the given world-space rectangle.
    /// </summary>
    public List<EntityUid> GetEntitiesInArea(Rectangle area, bool requireVisible = true)
    {
        var results = new List<EntityUid>();

        foreach (var (entUid, transform) in GetEntitiesWithComp<TransformComponent>())
        {
            if (requireVisible && !transform.Visible)
                continue;

            if (!area.Contains((int)transform.Position.X, (int)transform.Position.Y))
                continue;

            results.Add(entUid);
        }

        return results;
    }
}
