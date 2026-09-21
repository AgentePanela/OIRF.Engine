using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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
        if (ent.NetId.IsValid)
            _netToUid.Remove(ent.NetId);
    }
}
