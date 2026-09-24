using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Lidgren.Network;

namespace Engine.Client.GameStates;

public sealed partial class ClientGameStateSystem
{
    private readonly HashSet<NetEntity> _entering = new();
    private readonly HashSet<int> _currentComps = new();
    private readonly List<Type> _staleComps = new();

    private NetEntity _currentEntity = NetEntity.Invalid;
    private EntityUid _currentUid = EntityUid.Empty;
    private bool _currentKnown;
    private bool _currentEntering;
    private bool _isFullState;

    private void ApplyState(GameStateMessage msg)
    {
        _isFullState = msg.State.IsFullState;
        _seen.Clear();
        _entering.Clear();
        _metrics.BeginApply(msg.State.ToTick);

        // everything the state mentions has to exist before any component is read, since a component state can
        // point at another entity of the same state
        foreach (var entering in msg.State.Entering)
            EnterEntity(entering);

        foreach (var netEnt in msg.State.Deletions)
            DropEntity(netEnt);

        foreach (var netEnt in msg.State.LeftView)
            DropEntity(netEnt);

        var buffer = new NetBuffer();
        buffer.Write(msg.Blocks, 0, msg.BlocksLength);
        buffer.Position = 0;

        try
        {
            GameStateSerializer.ApplyBlocks(buffer, msg.BlockCount, _compFac, this);
        }
        catch (GameStateDesyncException e)
        {
            Log.Error($"[ClientGameState] {e.Message} The networked component hashes disagree, ask for everything again.");
            RequestFullState();
        }
        finally
        {
            FinishEntity();
        }

        if (_isFullState)
            DeleteMissing();

        _metrics.EndApply(_net.MySession?.Ping ?? 0, _processor.QueuedCount);
    }

    private void EnterEntity(EnteringEntity entering)
    {
        // the server keeps sending these until the client acks them, so it usually already has it
        if (_entManager.TryGetEntity(entering.NetEntity, out _))
            return;

        var uid = entering.ProtoId.Length == 0
            ? CreateEmptyEntity(null/*, _sceneMan.CurrentScene*/)
            : CreateEntity(entering.ProtoId/*, _sceneMan.CurrentScene*/);

        _entManager.RegisterNetEntity(uid, entering.NetEntity);
        _entering.Add(entering.NetEntity);
    }

    private void DropEntity(NetEntity netEntity)
    {
        if (_entManager.TryGetEntity(netEntity, out var uid))
            DeleteEntity(uid);

        _metrics.RecordEntity(netEntity, ClientGameStateMetrics.NetEntState.Leave,
            _origin.GetValueOrDefault(netEntity), "");

        _origin.Remove(netEntity);
    }

    /// <summary>
    /// On a full state whatever is not in it is not in the view anymore.
    /// </summary>
    private void DeleteMissing()
    {
        _toDelete.Clear();
        foreach (var netEnt in _entManager.ReplicatedEntities)
        {
            if (!_seen.Contains(netEnt))
                _toDelete.Add(netEnt);
        }

        foreach (var netEnt in _toDelete)
            DropEntity(netEnt);

        _toDelete.Clear();
    }

    void IGameStateApplier.BeginEntity(NetEntity netEntity, EntityBlockKind kind)
    {
        FinishEntity();

        _seen.Add(netEntity);
        _origin[netEntity] = kind;

        _currentEntity = netEntity;
        _currentEntering = _entering.Contains(netEntity);
        _currentKnown = _entManager.TryGetEntity(netEntity, out _currentUid);
        _currentComps.Clear();

        var state = _currentEntering
            ? ClientGameStateMetrics.NetEntState.Enter
            : ClientGameStateMetrics.NetEntState.Data;
        _metrics.RecordEntity(netEntity, state, kind, _entManager.GetEntity(_currentUid)?.Id.Id ?? "");

        // only reachable if the state that made this entity enter was lost AND the server already counted it as acked
        if (!_currentKnown)
        {
            Log.Error($"[ClientGameState] Got a state for the unknown {netEntity}.");
            RequestFullState();
        }
    }

    void IGameStateApplier.RemoveComponent(NetEntity netEntity, int netId)
    {
        if (!_currentKnown || _compFac.GetTypeByNetId(netId) is not { } type)
            return;

        _entManager.RemComp(_currentUid, type);
    }

    void IGameStateApplier.ApplyComponentState(NetEntity netEntity, int netId, IComponentState state)
    {
        if (!_currentKnown || _compFac.GetTypeByNetId(netId) is not { } type)
            return;

        var comp = EnsureComponent(type, out var created);
        comp.HandleNetState(state);

        _currentComps.Add(netId);
        RaiseEvent(_currentUid, new ComponentStateAppliedEvent { Component = comp, FirstState = created });
    }

    void IGameStateApplier.ApplyComponentData(NetEntity netEntity, int netId, NetBuffer buffer)
    {
        var type = _compFac.GetTypeByNetId(netId)!;

        if (!_currentKnown)
        {
            // the payload carries no length, so it has to be read even with nowhere to put it or everything after it
            // in the block is garbage.
            ((Component)Activator.CreateInstance(type)!).ReadNetState(buffer, _entManager);
            return;
        }

        var comp = EnsureComponent(type, out var created);
        comp.ReadNetState(buffer, _entManager);

        _currentComps.Add(netId);
        RaiseEvent(_currentUid, new ComponentStateAppliedEvent { Component = comp, FirstState = created });
    }

    private Component EnsureComponent(Type type, out bool created)
    {
        if (_entManager.TryComp(_currentUid, type, out var comp))
        {
            created = false;
            return comp;
        }

        created = true;
        return _entManager.AddComponent(_currentUid, type)!;
    }

    private void FinishEntity()
    {
        if (!_currentEntity.IsValid)
            return;

        if (_currentKnown)
        {
            if (_isFullState)
                RemoveStaleComponents();

            RaiseEvent(_currentUid, new EntityStateAppliedEvent { Entering = _currentEntering });
        }

        _currentEntity = NetEntity.Invalid;
        _currentUid = EntityUid.Empty;
        _currentKnown = false;
        _currentEntering = false;
    }

    /// <summary>
    /// A full state lists everything the entity has, so a networked component missing from it is a component the
    /// server does not have anymore.
    /// </summary>
    private void RemoveStaleComponents()
    {
        foreach (var type in _compFac.NetworkedTypes)
        {
            // a manual component returning null means nothing to send
            if (_compFac.IsManualState(type))
                continue;

            if (!_entManager.TryComp(_currentUid, type, out _))
                continue;

            if (_currentComps.Contains(_compFac.GetNetId(type)))
                continue;

            _staleComps.Add(type);
        }

        foreach (var type in _staleComps)
            _entManager.RemComp(_currentUid, type);

        _staleComps.Clear();
    }
}
