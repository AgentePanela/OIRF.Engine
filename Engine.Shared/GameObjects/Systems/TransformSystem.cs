using System;
using System.Collections.Generic;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

/// <summary>
/// Raised on an entity changes it Position or Angle in the Transform.
/// </summary>
public sealed class MoveEvent : EntityEvent
{
}

// runs before EVERYONE AHAHHJAHAHGAHGBAYHAYHHYAHAHBNA\Ha
[SystemPriority(-999)]
public sealed class TransformSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    // Last Position/Angle seen per entity, last tick
    private readonly Dictionary<EntityUid, (Vector2 Pos, float Angle)> _lastTransform = new();

    // Reused and re-raised every time an entity moves instead of a fresh MoveEvent per ent
    private readonly MoveEvent _moveEvent = new();

    private int _maxParents = 64;


    private readonly List<EntityUid> _childBuffer = new();
    private readonly Stack<List<EntityUid>> _movePool = new();

    // Parent > children
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _childrenByParent = new();

    private static readonly HashSet<EntityUid> NoChildren = new();

    public override void Init()
    {
        base.Init();
        SubscribeEvent<TransformComponent, CompAddedEvent>(OnCompAdded);
        SubscribeEvent<TransformComponent, CompRemovedEvent>(OnCompRemoved);
        
        _cfg.Subs(EngineCvars.TransformMaxParents, (v) => _maxParents = v, true);
    }

    // a transform deserialized from a prototype already has its Parent set, but had no Owner while it was being
    // filled, so its setter could not index it
    private void OnCompAdded(EntityUid uid, TransformComponent comp, CompAddedEvent args)
        => ReparentChild(uid, null, comp.Parent);

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

            _moveEvent.Handled = false;
            RaiseEvent(uid, _moveEvent);

            MoveChildren(uid, posDelta, angleDelta, 0);
        }
    }

    private void MoveChildren(EntityUid parentUid, Vector2 posDelta, float angleDelta, int depth)
    {
        if (depth >= _maxParents)
        {
            Log.Error($"{parentUid} has more than {_maxParents} levels of parenting deep.");
            return;
        }

        var children = _movePool.Count > 0 ? _movePool.Pop() : new List<EntityUid>();
        children.AddRange(GetChildren(parentUid));

        foreach (var uid in children)
        {
            if (!TryComp<TransformComponent>(uid, out var t))
                continue;

            t.Position += posDelta;
            t.Angle += angleDelta;
            _lastTransform[uid] = (t.Position, t.Angle);

            _moveEvent.Handled = false;
            RaiseEvent(uid, _moveEvent);

            MoveChildren(uid, posDelta, angleDelta, depth + 1);
        }

        children.Clear();
        _movePool.Push(children);
    }

    private void OnCompRemoved(EntityUid uid, TransformComponent comp, CompRemovedEvent args)
    {
        _lastTransform.Remove(uid);

        if (GetEntity(uid)?.Deleting is not false)
        {
            _childBuffer.Clear();
            _childBuffer.AddRange(GetChildren(uid));

            foreach (var child in _childBuffer)
                DeleteEntity(child);
        }

        ReparentChild(uid, comp.Parent, null);

        // whatever was hanging off it is not hanging off anything any more
        _childrenByParent.Remove(uid);
    }

    internal void ReparentChild(EntityUid child, EntityUid? from, EntityUid? to)
    {
        if (EntityUid.IsInvalid(child))
            return;

        if (from is { } old && _childrenByParent.TryGetValue(old, out var set))
        {
            set.Remove(child);

            // otherwise the dictionary grows forever with parents
            if (set.Count == 0)
                _childrenByParent.Remove(old);
        }

        if (to is not { } parent)
            return;

        if (!_childrenByParent.TryGetValue(parent, out var children))
            _childrenByParent[parent] = children = new HashSet<EntityUid>();

        children.Add(child);
    }

    #region API

    /// <summary>
    /// The entities parented to <paramref name="uid"/>.
    /// </summary>
    public IReadOnlyCollection<EntityUid> GetChildren(EntityUid uid)
        => _childrenByParent.TryGetValue(uid, out var set) ? set : NoChildren;

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
