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

    private readonly Stack<EntityState> _statePool = new();
    private readonly List<EntityState> _rentedStates = new();
    private readonly Stack<EntityBlock> _blockPool = new();
    private readonly List<EntityBlock> _rentedBlocks = new();

    // per session, reused
    private readonly List<PvsChunkHit> _hits = new();
    private readonly HashSet<PvsChunkKey> _chunksNow = new();
    private readonly HashSet<EntityUid> _accepted = new();
    private int _enterBudgetLeft;

    private bool _pvsEnabled;
    private float _pvsRange;
    private float _pvsLeaveMargin;
    private int _pvsEnterBudget;

    private void InitView()
    {
        SubscribeEvent<EntityAddedEvent>(OnEntityAdded);
        SubscribeEvent<EntityRemovedEvent>(OnEntityRemoved);

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

    private void GetVisibleBlocks(PvsSession session, List<EntityBlock> output)
    {
        _rooms.IsInRoom(session.Session, out var room);

        if (!_pvsEnabled)
        {
            output.Add(FillWholeBlock(EntityBlockKind.Global, _globals));

            if (room is not null)
                output.Add(FillWholeBlock(EntityBlockKind.Room, room.OwnedEntities));

            return;
        }

        var globals = RentBlock(EntityBlockKind.Global);
        output.Add(globals);

        EntityBlock? roomBlock = null;
        if (room is not null)
        {
            roomBlock = RentBlock(EntityBlockKind.Room);
            output.Add(roomBlock);
        }

        _accepted.Clear();
        _chunksNow.Clear();
        _enterBudgetLeft = _pvsEnterBudget <= 0 ? int.MaxValue : _pvsEnterBudget;

        // what the session looks through and what was forced to it are never culled, and go in before anything else
        // can eat the budget
        foreach (var uid in session.Viewers)
            Accept(session, uid, globals, roomBlock, room, forced: true);

        foreach (var uid in _pvs.GetGlobal())
            Accept(session, uid, globals, roomBlock, room, forced: true);

        if (room is not null)
        {
            foreach (var uid in _pvs.GetForScene(room))
                Accept(session, uid, globals, roomBlock, room, forced: true);
        }

        foreach (var uid in _pvs.GetForSession(session.Session))
            Accept(session, uid, globals, roomBlock, room, forced: true);

        CollectChunks(session);

        foreach (var hit in _hits)
        {
            // two eyes can reach the same chunk
            if (!_chunksNow.Add(hit.Key))
                continue;

            foreach (var uid in hit.Entities)
                Accept(session, uid, globals, roomBlock, room, forced: false);
        }

        session.SeenChunks.Clear();
        foreach (var key in _chunksNow)
            session.SeenChunks.Add(key);
    }

    /// <summary>
    /// Every chunk the session's eyes reach, closest first
    /// </summary>
    private void CollectChunks(PvsSession session)
    {
        _hits.Clear();

        foreach (var uid in session.Viewers)
        {
            if (!_entManager.TryComp<EyeComponent>(uid, out var eye) || !eye.Active)
                continue;

            if (!_entManager.TryComp<TransformComponent>(uid, out var xform) || !_entManager.HasEntity(uid, out var ent))
                continue;

            var center = xform.Position + eye.Offset;
            var range = MathF.Min(eye.Range ?? _pvsRange, _pvsRange);

            AddHits(session, null, center, range);

            // the eye own scene, not the session room
            if (ent.Scene is not null)
                AddHits(session, ent.Scene, center, range);
        }

        _hits.Sort(static (a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
    }

    private void AddHits(PvsSession session, IEntityScene? scene, Vector2 center, float range)
    {
        var first = _hits.Count;
        _index.GetChunksInRange(scene, center, range + _pvsLeaveMargin, _hits);

        // a chunk enters within range, but only leaves past range + margin - without that, walking the edge of the
        // view makes everything flip in and out every tick
        var rangeSq = range * range;
        for (var i = _hits.Count - 1; i >= first; i--)
        {
            if (_hits[i].DistanceSq <= rangeSq || session.SeenChunks.Contains(_hits[i].Key))
                continue;

            _hits.RemoveAt(i);
        }
    }

    /// <summary>
    /// Takes an entity into the session's view, with its ancestors. <paramref name="forced"/> skips the enter budget.
    /// </summary>
    private void Accept(PvsSession session, EntityUid uid, EntityBlock globals, EntityBlock? roomBlock, Room? room, bool forced)
    {
        if (_accepted.Contains(uid))
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
            if (_enterBudgetLeft <= 0)
                return;

            _enterBudgetLeft--;
        }

        _accepted.Add(uid);
        block.Entities.Add(RentEntityState(ent.NetId));

        // a child whose parent was culled would arrive pointing at a NetEntity the client does not have.
        if (_entManager.TryComp<TransformComponent>(uid, out var xform) && xform.Parent is { } parent)
            Accept(session, parent, globals, roomBlock, room, forced: true);
    }

    /// <summary>
    /// Everything replicated. Only reachable with net.pvs off.
    /// </summary>
    private EntityBlock FillWholeBlock(EntityBlockKind kind, IReadOnlyCollection<EntityUid> uids)
    {
        var block = RentBlock(kind);

        foreach (var uid in uids)
        {
            if (!_entManager.HasEntity(uid, out var ent) || !IsReplicated(ent))
                continue;

            block.Entities.Add(RentEntityState(ent.NetId));
        }

        return block;
    }

    private EntityBlock RentBlock(EntityBlockKind kind)
    {
        var block = _blockPool.Count > 0 ? _blockPool.Pop() : new EntityBlock();
        block.Reset(kind);
        _rentedBlocks.Add(block);
        return block;
    }

    private EntityState RentEntityState(NetEntity netEntity)
    {
        var state = _statePool.Count > 0 ? _statePool.Pop() : new EntityState();
        state.Reset(netEntity);
        _rentedStates.Add(state);
        return state;
    }

    private void ReturnRented()
    {
        foreach (var state in _rentedStates)
            _statePool.Push(state);

        foreach (var block in _rentedBlocks)
            _blockPool.Push(block);

        _rentedStates.Clear();
        _rentedBlocks.Clear();
    }

    private void OnEntityAdded(EntityAddedEvent ev)
    {
        if (_entManager.HasEntity(ev.Uid, out var ent) && ent.Scene is null)
            _globals.Add(ev.Uid);
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
