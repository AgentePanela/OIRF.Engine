using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

public sealed class TransformSystem : EntitySystem
{
    // Last Position/Angle seen per entity, last tick
    private readonly Dictionary<EntityUid, (Vector2 Pos, float Angle)> _lastTransform = new();

    public override void Init()
    {
        base.Init();
        SubscribeEvent<TransformComponent, CompRemovedEvent>(OnCompRemoved);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        foreach (var (uid, t) in GetEntitiesWithComp<TransformComponent>())
        {
            if (!_lastTransform.TryGetValue(uid, out var last))
            {
                _lastTransform[uid] = (t.Position, t.Angle);
                continue;
            }

            var posDelta = t.Position - last.Pos;
            var angleDelta = t.Angle - last.Angle;
            _lastTransform[uid] = (t.Position, t.Angle);

            if (posDelta == Vector2.Zero && angleDelta == 0f)
                continue;

            MoveChildren(uid, posDelta, angleDelta);
        }
    }

    private void MoveChildren(EntityUid parentUid, Vector2 posDelta, float angleDelta)
    {
        foreach (var (uid, t) in GetEntitiesWithComp<TransformComponent>())
        {
            if (t.Parent != parentUid)
                continue;

            t.Position += posDelta;
            t.Angle += angleDelta;
            _lastTransform[uid] = (t.Position, t.Angle);

            MoveChildren(uid, posDelta, angleDelta);
        }
    }

    private void OnCompRemoved(EntityUid uid, TransformComponent comp, CompRemovedEvent args)
    {
        _lastTransform.Remove(uid);

        if (GetEntity(uid)?.Deleting is false)
            return;

        var ents = GetChildren(comp);
        foreach (var puid in ents)
            DeleteEntity(puid);
    }

    private List<EntityUid> GetChildren(TransformComponent comp)
    {
        List<EntityUid> children = new();
        var query = GetEntitiesWithComp<TransformComponent>();
        foreach (var ent in query)
        {
            if (ent.comp.Parent == comp.Owner)
                children.Add(ent.uid);
        }

        return children;
    }

    #region API

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

    #endregion
}
