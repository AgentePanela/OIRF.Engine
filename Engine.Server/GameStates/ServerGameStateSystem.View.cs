using System;
using System.Collections.Generic;
using Engine.Server.Rooms;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Server.GameStates;

public sealed partial class ServerGameStateSystem
{
    [Dependency] private readonly PvsSystem _pvs = default!;

    // entities with no scene. kept as we go instead of walking every entity each tick just to find them.
    private readonly HashSet<EntityUid> _globals = new();

    private readonly PvsChunkIndex _index = new();

    private bool _pvsEnabled;
    private float _pvsRange;
    private float _pvsLeaveMargin;
    private int _pvsEnterBudget;

    private void InitView()
    {
        SubscribeEvent<EntityAddedEvent>(OnEntityAdded);
        SubscribeEvent<EntityRemovedEvent>(OnEntityRemoved);
        SubscribeEvent<EntitySceneChangedEvent>(OnEntitySceneChanged);

        _configMan.Subs(NetworkingCvars.NetPvs, value => _pvsEnabled = value);
        _configMan.Subs(NetworkingCvars.NetPvsRange, value => _pvsRange = value);
        _configMan.Subs(NetworkingCvars.NetPvsLeaveMargin, value => _pvsLeaveMargin = value);
        _configMan.Subs(NetworkingCvars.NetPvsEnterBudget, value => _pvsEnterBudget = value);
        _configMan.Subs(NetworkingCvars.NetPvsChunkSize, value => _index.ChunkSize = value);

        // anything that already exists by the time this system starts never raised its added event to us
        foreach (var uid in _entManager.GetEntities())
        {
            if (_entManager.HasEntity(uid, out var ent) && ent.Scene is null)
                _globals.Add(uid);
        }
    }

    /// <summary>
    /// Puts every replicated entity in its chunk
    /// </summary>
    private void RebuildIndex()
    {
        if (!_pvsEnabled)
            return;

        _index.Clear();

        foreach (var uid in _globals)
            IndexEntity(null, uid);

        foreach (var (_, room) in _rooms.AvailableRooms)
        {
            if (room.Sessions.Count == 0)
                continue;

            foreach (var uid in room.OwnedEntities)
                IndexEntity(room, uid);
        }
    }

    private void IndexEntity(IEntityScene? scene, EntityUid uid)
    {
        if (!_entManager.HasEntity(uid, out var ent) || !IsReplicated(ent))
            return;

        // no position means no chunk - it only goes out through an override, see PvsOverrides
        if (!_entManager.TryComp<TransformComponent>(uid, out var xform))
            return;

        _index.Add(scene, uid, xform.Position);
    }

    private void GetVisibleBlocks(PvsSession session, PvsBuildContext ctx)
    {
        var output = ctx.State.Blocks;
        _rooms.IsInRoom(session.Session, out var room);

        if (!_pvsEnabled)
        {
            output.Add(FillWholeBlock(ctx, EntityBlockKind.Global, _globals));

            if (room is not null)
                output.Add(FillWholeBlock(ctx, EntityBlockKind.Room, room.OwnedEntities));

            return;
        }

        var globals = ctx.RentBlock(EntityBlockKind.Global);
        output.Add(globals);

        EntityBlock? roomBlock = null;
        if (room is not null)
        {
            roomBlock = ctx.RentBlock(EntityBlockKind.Room);
            output.Add(roomBlock);
        }

        ctx.Accepted.Clear();
        ctx.ChunksNow.Clear();
        ctx.EnterBudgetLeft = _pvsEnterBudget <= 0 ? int.MaxValue : _pvsEnterBudget;

        // what the session looks through and what was forced to it are never culled, and go in before anything else
        // can eat the budget
        foreach (var uid in session.Viewers)
            Accept(session, ctx, uid, globals, roomBlock, room, forced: true);

        foreach (var uid in _pvs.GetGlobal())
            Accept(session, ctx, uid, globals, roomBlock, room, forced: true);

        if (room is not null)
        {
            foreach (var uid in _pvs.GetForScene(room))
                Accept(session, ctx, uid, globals, roomBlock, room, forced: true);
        }

        foreach (var uid in _pvs.GetForSession(session.Session))
            Accept(session, ctx, uid, globals, roomBlock, room, forced: true);

        CollectChunks(session, ctx);

        foreach (var hit in ctx.Hits)
        {
            // two eyes can reach the same chunk
            if (!ctx.ChunksNow.Add(hit.Key))
                continue;

            foreach (var uid in hit.Entities)
                Accept(session, ctx, uid, globals, roomBlock, room, forced: false);
        }

        session.SeenChunks.Clear();
        foreach (var key in ctx.ChunksNow)
            session.SeenChunks.Add(key);
    }

    /// <summary>
    /// Every chunk the session's eyes reach, closest first
    /// </summary>
    private void CollectChunks(PvsSession session, PvsBuildContext ctx)
    {
        ctx.Hits.Clear();

        foreach (var uid in session.Viewers)
        {
            if (!_entManager.TryComp<EyeComponent>(uid, out var eye) || !eye.Active)
                continue;

            if (!_entManager.TryComp<TransformComponent>(uid, out var xform) || !_entManager.HasEntity(uid, out var ent))
                continue;

            var center = xform.Position + eye.Offset;
            var range = MathF.Min(eye.Range ?? _pvsRange, _pvsRange);

            AddHits(session, ctx, null, center, range);

            // the eye own scene, not the session room
            if (ent.Scene is not null)
                AddHits(session, ctx, ent.Scene, center, range);
        }

        ctx.Hits.Sort(static (a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
    }

    private void AddHits(PvsSession session, PvsBuildContext ctx, IEntityScene? scene, Vector2 center, float range)
    {
        var first = ctx.Hits.Count;
        _index.GetChunksInRange(scene, center, range + _pvsLeaveMargin, ctx.Hits);

        // a chunk enters within range, but only leaves past range + margin - without that, walking the edge of the
        // view makes everything flip in and out every tick
        var rangeSq = range * range;
        for (var i = ctx.Hits.Count - 1; i >= first; i--)
        {
            if (ctx.Hits[i].DistanceSq <= rangeSq || session.SeenChunks.Contains(ctx.Hits[i].Key))
                continue;

            ctx.Hits.RemoveAt(i);
        }
    }

    /// <summary>
    /// Takes an entity into the session's view, with its ancestors. <paramref name="forced"/> skips the enter budget.
    /// </summary>
    private void Accept(PvsSession session, PvsBuildContext ctx, EntityUid uid, EntityBlock globals, EntityBlock? roomBlock, Room? room, bool forced)
    {
        if (ctx.Accepted.Contains(uid))
            return;

        if (!_entManager.HasEntity(uid, out var ent) || !IsReplicated(ent))
            return;

        // an override can name an entity of a room this session is not in
        if (ent.Scene is not null && !ReferenceEquals(ent.Scene, room))
            return;

        var block = ent.Scene is null ? globals : roomBlock;
        if (block is null)
            return;

        // only what the session does not have yet costs budget. Knows() is ack-based, so an entity that was sent and
        // not confirmed keeps counting
        if (!forced && !session.Knows(ent.NetId))
        {
            if (ctx.EnterBudgetLeft <= 0)
                return;

            ctx.EnterBudgetLeft--;
        }

        ctx.Accepted.Add(uid);
        block.Entities.Add(ctx.RentEntityState(ent.NetId));

        // a child whose parent was culled would arrive pointing at a NetEntity the client does not have.
        if (_entManager.TryComp<TransformComponent>(uid, out var xform) && xform.Parent is { } parent)
            Accept(session, ctx, parent, globals, roomBlock, room, forced: true);
    }

    /// <summary>
    /// Everything replicated. Only reachable with net.pvs off.
    /// </summary>
    private EntityBlock FillWholeBlock(PvsBuildContext ctx, EntityBlockKind kind, IReadOnlyCollection<EntityUid> uids)
    {
        var block = ctx.RentBlock(kind);

        foreach (var uid in uids)
        {
            if (!_entManager.HasEntity(uid, out var ent) || !IsReplicated(ent))
                continue;

            block.Entities.Add(ctx.RentEntityState(ent.NetId));
        }

        return block;
    }




    private void OnEntityAdded(EntityAddedEvent ev)
    {
        if (_entManager.HasEntity(ev.Uid, out var ent) && ent.Scene is null)
            _globals.Add(ev.Uid);
    }

    private void OnEntitySceneChanged(EntitySceneChangedEvent ev)
    {
        if (ev.New is null)
            _globals.Add(ev.Uid);
        else
            _globals.Remove(ev.Uid);
    }

    private void OnEntityRemoved(EntityRemovedEvent ev)
    {
        _globals.Remove(ev.Uid);

        // viewers are the one piece of PVS state not derived from the world, so they are the one that can be left
        // pointing at something gone.
        foreach (var (_, session) in _sessions)
            session.Viewers.Remove(ev.Uid);
    }
}
