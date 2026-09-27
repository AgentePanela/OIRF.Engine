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

    // server only: which components left which entity and when.
    private readonly Dictionary<NetEntity, List<(GameTick Tick, int NetId)>> _compRemovalHistory = new();
    private readonly List<NetEntity> _emptyRemovalEntries = new();

    /// <summary>
    /// The instance the generated dirty setters reach for, so they don't pay an IoC lookup per assignment.
    /// </summary>
    internal static EntityManager? Instance { get; private set; }

    /// <summary>
    /// Marks a component as changed on this tick, without saying which field
    /// </summary>
    public void Dirty(Component comp)
    {
        var tick = _timing.CurTick;
        comp.LastModifiedTick = tick;

        for (var i = 0; i < comp.NetFieldCount; i++)
            comp.SetFieldTick(i, tick);

        if (_entities.TryGetValue(comp.Owner, out var ent))
            ent.LastModifiedTick = tick;
    }

    /// <inheritdoc cref="Dirty(Component)"/>
    public void Dirty(EntityUid uid, Component comp)
    {
        var tick = _timing.CurTick;
        comp.LastModifiedTick = tick;

        for (var i = 0; i < comp.NetFieldCount; i++)
            comp.SetFieldTick(i, tick);

        if (_entities.TryGetValue(uid, out var ent))
            ent.LastModifiedTick = tick;
    }

    /// <summary>
    /// Marks a single field of a component as changed on this tick, so a delta only carries that one.
    /// </summary>
    public void Dirty(Component comp, int fieldIndex)
    {
        var tick = _timing.CurTick;
        comp.LastModifiedTick = tick;
        comp.SetFieldTick(fieldIndex, tick);

        if (_entities.TryGetValue(comp.Owner, out var ent))
            ent.LastModifiedTick = tick;
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

        foreach (var (netEnt, removals) in _compRemovalHistory)
        {
            removals.RemoveAll(entry => entry.Tick < oldestAck);
            if (removals.Count == 0)
                _emptyRemovalEntries.Add(netEnt);
        }

        foreach (var netEnt in _emptyRemovalEntries)
            _compRemovalHistory.Remove(netEnt);

        _emptyRemovalEntries.Clear();
    }

    /// <summary>
    /// Server only. The net ids of the components an entity lost after <paramref name="fromTick"/>.
    /// </summary>
    public void GetComponentsRemovedSince(NetEntity netEntity, GameTick fromTick, List<int> output)
    {
        if (fromTick == GameTick.Zero || !_compRemovalHistory.TryGetValue(netEntity, out var removals))
            return;

        foreach (var (tick, netId) in removals)
        {
            if (tick >= fromTick)
                output.Add(netId);
        }
    }

    /// <summary>
    /// Server only. Called as a component actually leaves an entity.
    /// </summary>
    internal void RecordComponentRemoval(Component comp)
    {
        if (!_contentMan.IsServer())
            return;

        // a deleted entity takes its components with it, and the entity deletion already tells the peers about it
        if (!_entities.TryGetValue(comp.Owner, out var ent) || !ent.NetId.IsValid || ent.Deleting)
            return;

        var netId = _compFac.GetNetId(comp.GetType());
        if (netId == 0)
            return;

        // losing a component is a change to the entity, and the entity tick is what decides whether a session skips
        // it entirely when building a delta
        ent.LastModifiedTick = _timing.CurTick;

        if (!_compRemovalHistory.TryGetValue(ent.NetId, out var removals))
            _compRemovalHistory[ent.NetId] = removals = new List<(GameTick, int)>();

        removals.Add((_timing.CurTick, netId));
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
        _compRemovalHistory.Remove(ent.NetId);

        if (_contentMan.IsServer())
            _deletionHistory.Add((_timing.CurTick, ent.NetId));
    }
}
