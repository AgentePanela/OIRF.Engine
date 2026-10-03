using System;
using System.Collections.Generic;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.Containers;
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

    // Last world Position/Angle seen per entity, last tick
    private readonly Dictionary<EntityUid, (Vector2 Pos, float Angle)> _lastTransform = new();

    // Reused and re-raised every time an entity moves instead of a fresh MoveEvent per ent
    private readonly MoveEvent _moveEvent = new();

    private int _maxParents = 64;


    private readonly List<EntityUid> _childBuffer = new();
    private readonly Stack<List<EntityUid>> _childPool = new();

    // Parent > children
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _childrenByParent = new();

    private static readonly HashSet<EntityUid> NoChildren = new();

    public override void Init()
    {
        base.Init();
        SubscribeEvent<TransformComponent, CompAddedEvent>(OnCompAdded);
        SubscribeEvent<TransformComponent, CompRemovedEvent>(OnCompRemoved);
        SubscribeEvent<TransformComponent, EntitySceneChangedEvent>(OnSceneChanged);

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
            if (HasComp<ContainedComponent>(uid))
                continue;

            var pos = t.Position;
            var angle = t.Angle;

            if (!_lastTransform.TryGetValue(uid, out var last))
            {
                _lastTransform[uid] = (pos, angle);
                continue;
            }

            if (pos == last.Pos && angle == last.Angle)
                continue;

            _lastTransform[uid] = (pos, angle);

            _moveEvent.Handled = false;
            RaiseEvent(uid, _moveEvent);
        }
    }

    private void OnCompRemoved(EntityUid uid, TransformComponent comp, CompRemovedEvent args)
    {
        _lastTransform.Remove(uid);

        _childBuffer.Clear();
        _childBuffer.AddRange(GetChildren(uid));

        if (GetEntity(uid)?.Deleting is not false)
        {
            foreach (var child in _childBuffer)
                DeleteEntity(child);
        }
        else
        {
            // the transform is still readable here, so the children can keep their world position
            foreach (var child in _childBuffer)
                Detach(child);
        }

        ReparentChild(uid, comp.Parent, null);

        // whatever was hanging off it is not hanging off anything any more
        _childrenByParent.Remove(uid);
    }

    private void OnSceneChanged(EntityUid uid, TransformComponent comp, EntitySceneChangedEvent args)
    {
        if (comp.Parent is { } parent && !EntityManager.ScenesInteract(GetScene(parent), args.New))
            Detach(uid, comp);

        // setScene on a child comes back here
        var children = _childPool.Count > 0 ? _childPool.Pop() : new List<EntityUid>();
        children.AddRange(GetChildren(uid));

        foreach (var child in children)
            SetScene(child, args.New);

        children.Clear();
        _childPool.Push(children);
    }

    /// <summary>
    /// Whether parenting <paramref name="child"/> to <paramref name="parent"/> would make it its own ancestor.
    /// </summary>
    internal bool WouldCycle(EntityUid child, EntityUid parent)
    {
        var current = parent;

        for (var depth = 0; depth < _maxParents; depth++)
        {
            if (current == child)
                return true;

            if (!TryComp<TransformComponent>(current, out var t) || t.Parent is not { } next)
                return false;

            current = next;
        }

        Log.Error($"{parent} has more than {_maxParents} levels of parenting deep.");
        return true;
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

        if (to is not { } parent || EntityUid.IsInvalid(parent))
            return;

        if (!_childrenByParent.TryGetValue(parent, out var children))
            _childrenByParent[parent] = children = new HashSet<EntityUid>();

        children.Add(child);
    }

    #region API

    /// <summary>
    /// Parents <paramref name="uid"/> to <paramref name="parent"/> (null unparents it).
    /// </summary>
    /// <param name="keepWorld">Keep the entity where it is in the world. False keeps its local values instead.</param>
    /// <returns>False if the entity has no transform or the parent would make a cycle.</returns>
    public bool SetParent(EntityUid uid, EntityUid? parent, bool keepWorld = true, TransformComponent? comp = null)
    {
        if (comp is null && !TryComp(uid, out comp))
            return false;

        var worldPos = comp.Position;
        var worldAngle = comp.Angle;

        comp.Parent = parent;

        // the setter refuses a cycle without throwing
        if (comp.Parent != parent)
            return false;

        if (keepWorld)
        {
            comp.Position = worldPos;
            comp.Angle = worldAngle;
        }

        return true;
    }

    /// <summary>
    /// Unparents <paramref name="uid"/>, keeping it where it is in the world.
    /// </summary>
    public void Detach(EntityUid uid, TransformComponent? comp = null)
        => SetParent(uid, null, keepWorld: true, comp);

    /// <summary>
    /// The entities parented to <paramref name="uid"/>.
    /// </summary>
    public IReadOnlyCollection<EntityUid> GetChildren(EntityUid uid)
        => _childrenByParent.TryGetValue(uid, out var set) ? set : NoChildren;

    /// <summary>
    /// Returns the closest entity from position within <paramref name="hitRadius"/> units, seen from
    /// <paramref name="scene"/>.
    /// </summary>
    /// <returns><see cref="EntityUid.Empty"/> if none found.</returns>
    public EntityUid GetEntityAtWorld(IEntityScene? scene, Vector2 worldPos, float hitRadius = 2f, bool requireVisible = true)
    {
        TryGetEntityAtWorld(scene, worldPos, out var uid, hitRadius, requireVisible);
        return uid;
    }

    /// <inheritdoc cref="GetEntityAtWorld(IEntityScene?, Vector2, float, bool)"/>
    public EntityUid GetEntityAtWorld(EntityUid from, Vector2 worldPos, float hitRadius = 2f, bool requireVisible = true)
        => GetEntityAtWorld(GetScene(from), worldPos, hitRadius, requireVisible);

    /// <summary>
    /// Tries to find the closest entity from the position within <paramref name="hitRadius"/> units, seen from
    /// <paramref name="scene"/>.
    /// </summary>
    public bool TryGetEntityAtWorld(
        IEntityScene? scene,
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

            if (HasComp<ContainedComponent>(entUid) || !EntityManager.ScenesInteract(scene, GetScene(entUid)))
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

    /// <inheritdoc cref="TryGetEntityAtWorld(IEntityScene?, Vector2, out EntityUid, float, bool)"/>
    public bool TryGetEntityAtWorld(
        EntityUid from,
        Vector2 worldPos,
        out EntityUid uid,
        float hitRadius = 2f,
        bool requireVisible = true)
            => TryGetEntityAtWorld(GetScene(from), worldPos, out uid, hitRadius, requireVisible);

    /// <summary>
    /// Returns all entities whose position falls within the given world-space rectangle, seen from
    /// <paramref name="scene"/>.
    /// </summary>
    public List<EntityUid> GetEntitiesInArea(IEntityScene? scene, Rectangle area, bool requireVisible = true)
    {
        var results = new List<EntityUid>();

        foreach (var (entUid, transform) in GetEntitiesWithComp<TransformComponent>())
        {
            if (requireVisible && !transform.Visible)
                continue;

            if (HasComp<ContainedComponent>(entUid) || !EntityManager.ScenesInteract(scene, GetScene(entUid)))
                continue;

            if (!area.Contains((int)transform.Position.X, (int)transform.Position.Y))
                continue;

            results.Add(entUid);
        }

        return results;
    }

    /// <inheritdoc cref="GetEntitiesInArea(IEntityScene?, Rectangle, bool)"/>
    public List<EntityUid> GetEntitiesInArea(EntityUid from, Rectangle area, bool requireVisible = true)
        => GetEntitiesInArea(GetScene(from), area, requireVisible);

    #endregion
}
