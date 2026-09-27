using System.Collections.Generic;
using Engine.Server.Rooms;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.Timing;

namespace Engine.Server.GameStates;

public sealed partial class ServerGameStateSystem
{
    // entities with no scene. kept as we go instead of walking every entity each tick just to find them.
    private readonly HashSet<EntityUid> _globals = new();

    private readonly Stack<EntityState> _statePool = new();
    private readonly List<EntityState> _rentedStates = new();
    private readonly Stack<EntityBlock> _blockPool = new();
    private readonly List<EntityBlock> _rentedBlocks = new();

    private void InitView()
    {
        SubscribeEvent<EntityAddedEvent>(OnEntityAdded);
        SubscribeEvent<EntityRemovedEvent>(OnEntityRemoved);

        // anything that already exists by the time this system starts never raised its added event to us
        foreach (var uid in _entManager.GetEntities())
        {
            if (_entManager.HasEntity(uid, out var ent) && ent.Scene is null)
                _globals.Add(uid);
        }
    }

    /// <summary>
    /// What a session is allowed to see. This is the only place that decides it 
    /// </summary>
    private void GetVisibleBlocks(PvsSession session, List<EntityBlock> output)
    {
        output.Add(FillBlock(EntityBlockKind.Global, _globals));

        if (_rooms.IsInRoom(session.Session, out var room))
            output.Add(FillBlock(EntityBlockKind.Room, room.OwnedEntities));
    }

    /// <summary>
    /// Returns a block holding every replicated entity of a group, with no state built yet.
    /// </summary>
    private EntityBlock FillBlock(EntityBlockKind kind, IReadOnlyCollection<EntityUid> uids)
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
        => _globals.Remove(ev.Uid);
}
