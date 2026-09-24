using System.Collections.Generic;
using Engine.Server.Rooms;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;

namespace Engine.Server.GameStates;

public sealed partial class ServerGameStateSystem
{
    // entities with no scene. kept as we go instead of walking every entity each tick just to find them
    private readonly HashSet<EntityUid> _globals = new();

    private readonly EntityBlock _globalBlock = new();
    private readonly Dictionary<Room, EntityBlock> _roomBlocks = new();
    private readonly List<Room> _staleRooms = new();

    private readonly Stack<EntityState> _statePool = new();
    private readonly List<EntityState> _rentedStates = new();

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
        output.Add(_globalBlock);

        if (_rooms.IsInRoom(session.Session, out var room) && _roomBlocks.TryGetValue(room, out var block))
            output.Add(block);
    }

    private void BuildBlocks()
    {
        ReturnEntityStates();

        _globalBlock.Reset(EntityBlockKind.Global);
        BuildBlock(_globalBlock, _globals);

        foreach (var (_, room) in _rooms.AvailableRooms)
        {
            if (room.Sessions.Count == 0)
                continue;

            if (!_roomBlocks.TryGetValue(room, out var block))
                _roomBlocks[room] = block = new EntityBlock();

            block.Reset(EntityBlockKind.Room);
            BuildBlock(block, room.OwnedEntities);
        }

        DropEmptyRoomBlocks();
    }

    private void DropEmptyRoomBlocks()
    {
        foreach (var (room, _) in _roomBlocks)
        {
            if (room.Sessions.Count == 0)
                _staleRooms.Add(room);
        }

        foreach (var room in _staleRooms)
            _roomBlocks.Remove(room);

        _staleRooms.Clear();
    }

    private EntityState RentEntityState(NetEntity netEntity)
    {
        var state = _statePool.Count > 0 ? _statePool.Pop() : new EntityState();
        state.Reset(netEntity);
        _rentedStates.Add(state);
        return state;
    }

    private void ReturnEntityStates()
    {
        foreach (var state in _rentedStates)
            _statePool.Push(state);

        _rentedStates.Clear();
    }

    private void OnEntityAdded(EntityAddedEvent ev)
    {
        if (_entManager.HasEntity(ev.Uid, out var ent) && ent.Scene is null)
            _globals.Add(ev.Uid);
    }

    private void OnEntityRemoved(EntityRemovedEvent ev)
        => _globals.Remove(ev.Uid);
}
