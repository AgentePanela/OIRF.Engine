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

        if (!_net.IsServer)
            return;

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

        BuildBlocks();

        foreach (var session in _sessions.Values)
        {
            ComputeSessionState(session);
            Send(session);
        }

        CullHistory();
    }

    /// <summary>
    /// Fills a block's <see cref="EntityBlock.Entities"/> and serializes it. A block is built once per tick and every
    /// session that can see it sends the same bytes.
    /// </summary>
    private void BuildBlock(EntityBlock block, IReadOnlyCollection<EntityUid> uids)
    {
        foreach (var uid in uids)
        {
            if (!_entManager.HasEntity(uid, out var ent) || !IsReplicated(ent))
                continue;

            block.Entities.Add(BuildEntityState(ent));
        }

        GameStateSerializer.WriteBlock(block, _entManager, _compFac);
    }

    private EntityState BuildEntityState(Entity ent)
    {
        var state = RentEntityState(ent.NetId);

        foreach (var type in _compFac.NetworkedTypes)
        {
            if (!_entManager.TryComp(ent.Uid, type, out var comp) || comp.Deleted)
                continue;

            var netId = _compFac.GetNetId(type);

            if (!_compFac.IsManualState(type))
            {
                state.Changes.Add(new ComponentChange(netId, comp));
                continue;
            }

            // a manual component says "nothing to send" by returning null, so it simply does not show up in the state
            var manual = comp.GetNetState(GameTick.Zero);
            if (manual is not null)
                state.Changes.Add(new ComponentChange(netId, manual));
        }

        return state;
    }

    private static bool IsReplicated(Entity ent)
        => ent.NetId.IsValid && !ent.Deleting;
}
