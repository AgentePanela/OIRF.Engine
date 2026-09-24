using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Engine.Shared.Timing;

namespace Engine.Shared.GameObjects;

public sealed partial class EntityManager
{
    // NetEntity -> EntityUid. The other way around is Entity.NetId.
    private readonly Dictionary<NetEntity, EntityUid> _netToUid = new();

    /// <summary>
    /// Server only: the next <see cref="NetEntity"/> to be handed out.
    /// </summary>
    private int _nextNetId = 1;

    /// <summary>
    /// Every <see cref="NetEntity"/> currently known locally (server: every entity it created | client: every
    /// entity the server told it about so far)
    /// </summary>
    public IReadOnlyCollection<NetEntity> ReplicatedEntities => _netToUid.Keys;

    private readonly List<(GameTick Tick, NetEntity NetEntity)> _deletionHistory = new(); // server only: what was deleted and when

    /// <summary>
    /// Marks a component as changed on this tick.
    /// </summary>
    public void Dirty(Component comp)
    {
        var tick = _timing.CurTick;
        comp.LastModifiedTick = tick;

        if (_entities.TryGetValue(comp.Owner, out var ent))
            ent.LastModifiedTick = tick;
    }

    /// <inheritdoc cref="Dirty(Component)"/>
    public void Dirty(EntityUid uid, Component comp)
    {
        comp.LastModifiedTick = _timing.CurTick;

        if (_entities.TryGetValue(uid, out var ent))
            ent.LastModifiedTick = comp.LastModifiedTick;
    }

    /// <summary>
    /// Server only. Every entity deleted after <paramref name="fromTick"/>. <see cref="GameTick.Zero"/> means full state.
    /// </summary>
    public void GetDeletedSince(GameTick fromTick, List<NetEntity> output)
    {
        if (fromTick == GameTick.Zero)
            return;

        foreach (var (tick, netEnt) in _deletionHistory)
        {
            if (tick >= fromTick)
                output.Add(netEnt);
        }
    }

    /// <summary>
    /// Drops the deletions every peer has already been told about.
    /// </summary>
    public void CullDeletionHistory(GameTick oldestAck)
    {
        if (oldestAck == GameTick.Zero)
            return;

        _deletionHistory.RemoveAll(entry => entry.Tick < oldestAck);
    }

    /// <summary>
    /// Get the <see cref="NetEntity"/> of an entity. <see cref="NetEntity.Invalid"/> if it does not have one.
    /// </summary>
    public NetEntity GetNetEntity(EntityUid uid)
        => _entities.TryGetValue(uid, out var ent) ? ent.NetId : NetEntity.Invalid;

    public bool TryGetNetEntity(EntityUid uid, out NetEntity netEnt)
    {
        netEnt = GetNetEntity(uid);
        return netEnt.IsValid;
    }

    /// <summary>
    /// Get the local <see cref="EntityUid"/> of a <see cref="NetEntity"/>. <see cref="EntityUid.Empty"/> if unknown.
    /// </summary>
    public EntityUid GetEntity(NetEntity netEnt)
        => _netToUid.TryGetValue(netEnt, out var uid) ? uid : EntityUid.Empty;

    public bool TryGetEntity(NetEntity netEnt, out EntityUid uid)
        => _netToUid.TryGetValue(netEnt, out uid);

    /// <summary>
    /// Server only. Gives a brand new <see cref="NetEntity"/> to an entity.
    /// </summary>
    private void AssignNetEntity(Entity ent)
    {
        var netEnt = new NetEntity(_nextNetId++);
        _netToUid[netEnt] = ent.Uid;
        ent.NetId = netEnt;
    }

    /// <summary>
    /// <strong>ONLY USE THIS IF YOU KNOW WHAT U ARE DOING</strong><para/>
    /// Client only. Links an entity to the <see cref="NetEntity"/> the server said it has.
    /// </summary>
    public void RegisterNetEntity(EntityUid uid, NetEntity netEnt)
    {
        if (_contentMan.IsServer())
            throw new System.InvalidOperationException("The server is the one who hands out NetEntities.");

        if (!netEnt.IsValid)
            throw new System.ArgumentException("Cannot register an invalid NetEntity.", nameof(netEnt));

        if (_netToUid.ContainsKey(netEnt))
            throw new System.InvalidOperationException($"{netEnt} is already linked to {_netToUid[netEnt]}.");

        if (!_entities.TryGetValue(uid, out var ent))
            throw new System.InvalidOperationException($"{uid} does not exist.");

        if (ent.NetId.IsValid)
            throw new System.InvalidOperationException($"{uid} already has {ent.NetId}.");

        _netToUid[netEnt] = uid;
        ent.NetId = netEnt;
    }

    private void ReleaseNetEntity(Entity ent)
    {
        if (!ent.NetId.IsValid)
            return;

        _netToUid.Remove(ent.NetId);

        if (_contentMan.IsServer())
            _deletionHistory.Add((_timing.CurTick, ent.NetId));
    }
}
