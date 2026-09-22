using System;
using System.Collections.Generic;
using Engine.Client.Rooms;
using Engine.Client.Scenes;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.GameStates;
using Engine.Shared.IoC;
using Engine.Shared.Networking;
using Engine.Shared.Timing;
using Lidgren.Network;

namespace Engine.Client.GameStates;

/// <summary>
/// Applies what the server replicates: creates, updates and drops the entities the local session can see.
/// </summary>
[SystemPriority(int.MinValue)]
public sealed partial class ClientGameStateSystem : EntitySystem, IGameStateApplier
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ComponentFactory _compFac = default!;
    [Dependency] private readonly SceneManager _sceneMan = default!;
    [Dependency] private readonly IRoomManager _rooms = default!;

    private readonly GameStateProcessor _processor = new();

    // where each replicated entity came from, so leaving a room only drops that room entities
    private readonly Dictionary<NetEntity, EntityBlockKind> _origin = new();

    private readonly HashSet<NetEntity> _seen = new();
    private readonly List<NetEntity> _toDelete = new();

    private GameTick _lastAckedTick = GameTick.Zero;
    private bool _fullStateRequested;

    public override void Init()
    {
        base.Init();

        // IsClient only flips on connect, and this system lives on the client either way
        _net.RegisterNetMessage<GameStateMessage>(OnGameState);
        _net.RegisterNetMessage<StateAckMessage>();
        _net.RegisterNetMessage<RequestFullStateMessage>();

        _net.OnDisconnected += (_, _) => ClearReplicated(null);
        _rooms.OnLeft += (_, _) => ClearReplicated(EntityBlockKind.Room);
    }

    public override void Update(float dt)
    {
        if (!_net.IsClient)
            return;

        if (!_processor.TryGetNext(out var msg))
        {
            if (_processor.NeedsFullState)
                RequestFullState();

            return;
        }

        ApplyState(msg);

        _processor.Applied(msg.State.ToTick);
        _fullStateRequested = false;

        // TODO: client-side tickrate
        _timing.SetTick(msg.State.ToTick);

        Ack(msg.State.ToTick);
    }

    private void OnGameState(GameStateMessage msg, INetSession? session)
        => _processor.Add(msg);

    private void Ack(GameTick tick)
    {
        if (tick <= _lastAckedTick)
            return;

        _lastAckedTick = tick;
        _net.MySession?.SendMessage(new StateAckMessage { Tick = tick });
    }

    private void RequestFullState()
    {
        if (_fullStateRequested)
            return;

        _fullStateRequested = true;
        _net.MySession?.SendMessage(new RequestFullStateMessage());
    }

    /// <summary>
    /// Drops the replicated entities of a kind, or all of them when <paramref name="kind"/> is null. The server
    /// tracks what this client has, so it has to be told to start over.
    /// </summary>
    public void ClearReplicated(EntityBlockKind? kind)
    {
        _toDelete.Clear();
        foreach (var (netEnt, origin) in _origin)
        {
            if (kind is null || origin == kind)
                _toDelete.Add(netEnt);
        }

        foreach (var netEnt in _toDelete)
        {
            DeleteEntity(_entManager.GetEntity(netEnt));
            _origin.Remove(netEnt);
        }

        _toDelete.Clear();
        _processor.Reset();
        _lastAckedTick = GameTick.Zero;

        if (_net.IsClient && _net.MySession is not null)
            RequestFullState();
    }
}
