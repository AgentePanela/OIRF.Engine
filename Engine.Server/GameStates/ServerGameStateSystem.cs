using System.Collections.Generic;
using Engine.Server.Rooms;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.GameStates;
using Engine.Shared.IoC;
using Engine.Shared.Networking;
using Engine.Shared.Timing;

namespace Engine.Server.GameStates;

/// <summary>
/// Builds what each session is allowed to see at the end of every tick and sends it over.
/// </summary>
[SystemPriority(int.MaxValue)] // after every other system had its say in this tick
public sealed partial class ServerGameStateSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ComponentFactory _compFac = default!;
    [Dependency] private readonly IRoomManager _rooms = default!;

    public override void Init()
    {
        base.Init();

        InitView();
        InitSessions();
    }

    public override void Update(float dt)
    {
        if (!_net.IsServer)
            return;

        EnsureSessions();
        if (_sessions.Count == 0)
            return;

        foreach (var session in _sessions.Values)
        {
            ComputeSessionState(session);
            Send(session);
            ReturnRented();
        }

        CullHistory();
    }

    /// <summary>
    /// Fills in what each visible entity has to say to this session, dropping the ones with nothing to say.
    /// </summary>
    private void BuildBlocks(PvsSession session, GameState state)
    {
        foreach (var block in state.Blocks)
        {
            for (var i = block.Entities.Count - 1; i >= 0; i--)
            {
                var entState = block.Entities[i];
                if (BuildEntityState(session, entState, state.FromTick))
                    continue;

                // nothing changed and the session already has it
                block.Entities.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// False when the entity has nothing to send to this session.
    /// </summary>
    private bool BuildEntityState(PvsSession session, EntityState entState, GameTick fromTick)
    {
        if (!_entManager.TryGetEntity(entState.NetEntity, out var uid) || !_entManager.HasEntity(uid, out var ent))
            return false;

        var entering = _enteringNow.Contains(entState.NetEntity);

        if (!entering && ent.LastModifiedTick < fromTick)
            return false;

        _entManager.GetComponentsRemovedSince(entState.NetEntity, fromTick, entState.Removed);

        foreach (var type in _compFac.NetworkedTypes)
        {
            if (!_entManager.TryComp(uid, type, out var comp) || comp.Deleted)
                continue;

            var netId = _compFac.GetNetId(type);

            // a component the session never saw has to go whole, whatever its fields say about the baseline
            var full = entering || fromTick == GameTick.Zero || comp.CreationTick >= fromTick;

            if (_compFac.IsManualState(type))
            {
                if (!full && comp.LastModifiedTick < fromTick)
                    continue;

                // a manual component says "nothing to send" by returning null
                var manual = comp.GetNetState(full ? GameTick.Zero : fromTick);
                if (manual is not null)
                    entState.Changes.Add(new ComponentChange(netId, manual));

                continue;
            }

            if (!full && comp.LastModifiedTick < fromTick)
                continue;

            entState.Changes.Add(new ComponentChange(netId, comp, full));
        }

        return entState.Changes.Count > 0 || entState.Removed.Count > 0;
    }

    private static bool IsReplicated(Entity ent)
        => ent.NetId.IsValid && !ent.Deleting;
}
