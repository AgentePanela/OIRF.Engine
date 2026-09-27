using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Lidgren.Network;

namespace Engine.Server.GameStates;

public sealed class PvsBuildContext
{
    public readonly GameState State = new();

    /// <summary>
    /// The chunks the eyes reached this tick, and which of them were taken.
    /// </summary>
    public readonly List<PvsChunkHit> Hits = new();
    public readonly HashSet<PvsChunkKey> ChunksNow = new();

    public readonly HashSet<EntityUid> Accepted = new();
    public int EnterBudgetLeft;

    public readonly HashSet<NetEntity> VisibleNow = new();
    public readonly HashSet<NetEntity> EnteringNow = new();
    public readonly List<NetEntity> GoneEntities = new();

    /// <summary>
    /// The message body, written while building so the send afterwards is just a copy.
    /// </summary>
    public readonly NetBuffer Body = new();

    private readonly Stack<EntityState> _statePool = new();
    private readonly List<EntityState> _rentedStates = new();
    private readonly Stack<EntityBlock> _blockPool = new();
    private readonly List<EntityBlock> _rentedBlocks = new();

    public EntityBlock RentBlock(EntityBlockKind kind)
    {
        var block = _blockPool.Count > 0 ? _blockPool.Pop() : new EntityBlock();
        block.Reset(kind);
        _rentedBlocks.Add(block);
        return block;
    }

    public EntityState RentEntityState(NetEntity netEntity)
    {
        var state = _statePool.Count > 0 ? _statePool.Pop() : new EntityState();
        state.Reset(netEntity);
        _rentedStates.Add(state);
        return state;
    }

    public void ReturnRented()
    {
        foreach (var state in _rentedStates)
            _statePool.Push(state);

        foreach (var block in _rentedBlocks)
            _blockPool.Push(block);

        _rentedStates.Clear();
        _rentedBlocks.Clear();
    }

    public void ResetBody()
    {
        Body.LengthBits = 0;
        Body.Position = 0;
    }
}
