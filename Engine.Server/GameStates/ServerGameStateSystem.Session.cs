using System.Collections.Generic;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.IoC;
using Engine.Shared.Networking;
using Engine.Shared.Timing;
using NetDeliveryMethod = Engine.Shared.Networking.NetDeliveryMethod;

namespace Engine.Server.GameStates;

public sealed partial class ServerGameStateSystem
{
    [Dependency] private readonly IConfigurationManager _configMan = default!;

    private readonly Dictionary<INetSession, PvsSession> _sessions = new();
    private readonly List<INetSession> _goneSessions = new();
    private readonly HashSet<NetEntity> _visibleNow = new();
    private readonly HashSet<NetEntity> _enteringNow = new();
    private readonly List<NetEntity> _goneEntities = new();

    private int _forceAckThreshold;
    private int _fullStateCooldown;
    private bool _deltaEnabled = true;

    private void InitSessions()
    {
        _net.RegisterNetMessage<StateAckMessage>(OnAck);
        _net.RegisterNetMessage<RequestFullStateMessage>(OnRequestFull);

        _configMan.Subs(NetworkingCvars.NetForceAckThreshold, value => _forceAckThreshold = value);
        _configMan.Subs(NetworkingCvars.NetFullStateCooldown, value => _fullStateCooldown = value);
        _configMan.Subs(NetworkingCvars.NetDelta, value => _deltaEnabled = value);
    }

    internal PvsSession GetSession(INetSession session)
    {
        if (!_sessions.TryGetValue(session, out var pvs))
            _sessions[session] = pvs = new PvsSession(session);

        return pvs;
    }

    private void EnsureSessions()
    {
        foreach (var session in _net.Sessions)
        {
            GetSession(session);
        }

        foreach (var (session, _) in _sessions)
        {
            if (!session.IsConnected)
                _goneSessions.Add(session);
        }

        foreach (var session in _goneSessions)
        {
            _sessions.Remove(session);
            _pvs.ClearSession(session);
        }

        _goneSessions.Clear();
    }

    private void OnAck(StateAckMessage msg, INetSession? session)
    {
        if (session is null || !_sessions.TryGetValue(session, out var pvs))
            return;

        // acks are unreliable, so they can show up out of order
        if (msg.Tick <= pvs.LastReceivedAck)
            return;

        pvs.LastReceivedAck = msg.Tick;

        // the session confirmed a state, this is what turns delta on
        if (pvs.RequestedFull)
            Log.Debug($"{pvs.Session.RemoteEndPoint} acked for {msg.Tick}.");

        pvs.RequestedFull = false;
    }

    private void OnRequestFull(RequestFullStateMessage msg, INetSession? session)
    {
        if (session is null || !_sessions.TryGetValue(session, out var pvs))
            return;

        if (pvs.LastFullStateRequest != GameTick.Zero &&
            _timing.CurTick - pvs.LastFullStateRequest < (uint)_fullStateCooldown)
        {
            pvs.IgnoredFullStateRequests++;
            if (pvs.IgnoredFullStateRequests % 100 == 0)
                Log.Warn($"{pvs.Session.RemoteEndPoint} asked for {pvs.IgnoredFullStateRequests} full states faster than the cooldown allows! Maybe a malicious client?");

            return;
        }

        pvs.LastFullStateRequest = _timing.CurTick;
        pvs.RequestedFull = true;
        pvs.ForceSendReliably = true;
        pvs.LastReceivedAck = GameTick.Zero;
        pvs.Sent.Clear();
        pvs.SeenChunks.Clear();
    }

    private void ComputeSessionState(PvsSession session)
    {
        var state = session.State;
        state.Reset();

        state.ToTick = _timing.CurTick;
        state.FromTick = session.RequestedFull || !_deltaEnabled ? GameTick.Zero : session.LastReceivedAck;

        _entManager.GetDeletedSince(state.FromTick, state.Deletions);

        GetVisibleBlocks(session, state.Blocks);
        CollectEntering(session, state);
        BuildBlocks(session, state);
    }

    /// <summary>
    /// Works out which of the visible entities this session does not have yet, so the prototype id and others only
    /// travel until the session confirms it, and which ones it has but cannot see anymore.
    /// </summary>
    private void CollectEntering(PvsSession session, GameState state)
    {
        _visibleNow.Clear();
        _enteringNow.Clear();

        foreach (var block in state.Blocks)
        {
            foreach (var ent in block.Entities)
            {
                _visibleNow.Add(ent.NetEntity);

                if (session.Knows(ent.NetEntity))
                    continue;

                var uid = _entManager.GetEntity(ent.NetEntity);
                var protoId = _entManager.HasEntity(uid, out var entity) ? entity.Id.Id ?? string.Empty : string.Empty;
                state.Entering.Add(new EnteringEntity(ent.NetEntity, protoId));
                _enteringNow.Add(ent.NetEntity);
                session.Sent.TryAdd(ent.NetEntity, state.ToTick);
            }
        }

        foreach (var (netEnt, _) in session.Sent)
        {
            if (!_visibleNow.Contains(netEnt))
                _goneEntities.Add(netEnt);
        }

        foreach (var netEnt in _goneEntities)
            session.Sent.Remove(netEnt);
    }

    private void Send(PvsSession session)
    {
        var unacked = _timing.CurTick - session.LastReceivedAck;
        var stale = session.LastReceivedAck != GameTick.Zero && unacked > (uint)_forceAckThreshold;

        session.Session.SendMessage(new GameStateMessage
        {
            State = session.State,
            EntMan = _entManager,
            Delivery = session.ForceSendReliably || stale
                ? NetDeliveryMethod.ReliableOrdered
                : NetDeliveryMethod.Unreliable,
        });

        session.ForceSendReliably = false;

        SendLeftView(session);
    }

    /// <summary>
    /// Leaving the view goes in its own reliable message so dont get lost in the networking :(
    /// </summary>
    private void SendLeftView(PvsSession session)
    {
        if (_goneEntities.Count == 0)
            return;

        var msg = new LeaveViewMessage();
        msg.Entities.AddRange(_goneEntities);
        session.Session.SendMessage(msg);

        _goneEntities.Clear();
    }

    /// <summary>
    /// Drops the history every session has already been told about.
    /// </summary>
    private void CullHistory()
    {
        var oldest = _timing.CurTick;
        foreach (var (_, session) in _sessions)
        {
            // a session with no baseline is never told about deletions in the first place
            if (session.RequestedFull)
                continue;

            if (session.LastReceivedAck < oldest)
                oldest = session.LastReceivedAck;
        }

        _entManager.CullDeletionHistory(oldest);
    }
}
