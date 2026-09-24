using System;
using System.Collections.Generic;
using Engine.Shared.Debug.Diagnostics;
using Engine.Shared.GameStates;
using Engine.Shared.IoC;
using Engine.Shared.Timing;

namespace Engine.Client.GameStates;

/// <summary>
/// What the replication stream is doing, kept around so the debug overlays and the profiler can show it.
/// </summary>
[RegisterIoC]
public sealed class ClientGameStateMetrics
{
    /// <summary>
    /// 5 seconds at 60Hz.
    /// </summary>
    public const int HistorySize = 300;

    /// <summary>
    /// How many ticks of per-entity traffic are kept.
    /// </summary>
    public const int EntityHistorySize = 64;

    public enum NetEntState : byte
    {
        Nothing,
        Data,
        Enter,
        Leave,
    }

    public readonly record struct StateSample(GameTick Tick, int Bytes, int Blocks, int Entities, int Entering,
        int Leaving, short Ping, int Queued);

    public sealed class EntityTraffic
    {
        public GameTick LastUpdate;
        public EntityBlockKind Origin;
        public string ProtoId = "";

        public readonly (GameTick Tick, NetEntState State)[] Traffic = new (GameTick, NetEntState)[EntityHistorySize];

        public void Record(GameTick tick, NetEntState state)
        {
            LastUpdate = tick;
            Traffic[tick.Value % EntityHistorySize] = (tick, state);
        }

        public NetEntState At(GameTick tick)
        {
            var slot = Traffic[tick.Value % EntityHistorySize];
            return slot.Tick == tick ? slot.State : NetEntState.Nothing;
        }

        /// <summary>
        /// How many of the last <see cref="EntityHistorySize"/> ticks this entity showed up in.
        /// </summary>
        public int Activity(GameTick newest)
        {
            var count = 0;
            foreach (var (tick, state) in Traffic)
            {
                if (state != NetEntState.Nothing && newest.Value - tick.Value < EntityHistorySize)
                    count++;
            }

            return count;
        }
    }

    private readonly CircularBuffer<StateSample> _history = new(HistorySize);
    private readonly Dictionary<NetEntity, EntityTraffic> _entities = new();
    private readonly List<NetEntity> _stale = new();

    private GameTick _applying = GameTick.Zero;
    private int _entering;
    private int _leaving;
    private int _entityCount;
    private int _receivedBytes;
    private int _receivedBlocks;

    /// <summary>
    /// The newest tick that was applied.
    /// </summary>
    public GameTick LastAppliedTick { get; private set; } = GameTick.Zero;

    /// <summary>
    /// The client asked for a full state and has not had one yet.
    /// </summary>
    public bool AwaitingFull { get; set; }

    public int SampleCount => _history.Count;

    public StateSample this[int index] => _history[index];

    public StateSample Latest => _history.Count == 0 ? default : _history[_history.Count - 1];

    public IReadOnlyDictionary<NetEntity, EntityTraffic> Entities => _entities;

    /// <summary>
    /// Averaged over the last second worth of samples, so it does not jump around with a single fat state.
    /// </summary>
    public double BytesPerSecond(float tickRate)
    {
        if (_history.Count == 0)
            return 0.0;

        var window = (int)MathF.Min(tickRate <= 0f ? 60f : tickRate, _history.Count);
        var total = 0L;
        for (var i = _history.Count - window; i < _history.Count; i++)
            total += _history[i].Bytes;

        return total * (tickRate / window);
    }

    public void RecordReceived(int bytes, int blocks)
    {
        _receivedBytes += bytes;
        _receivedBlocks = blocks;
    }

    public void BeginApply(GameTick tick)
    {
        _applying = tick;
        _entering = 0;
        _leaving = 0;
        _entityCount = 0;
    }

    public void RecordEntity(NetEntity netEnt, NetEntState state, EntityBlockKind origin, string protoId)
    {
        if (!_entities.TryGetValue(netEnt, out var traffic))
            _entities[netEnt] = traffic = new EntityTraffic();

        traffic.Origin = origin;
        if (protoId.Length > 0)
            traffic.ProtoId = protoId;

        traffic.Record(_applying, state);

        switch (state)
        {
            case NetEntState.Data:
                _entityCount++;
                break;
            case NetEntState.Enter:
                _entering++;
                _entityCount++;
                break;
            case NetEntState.Leave:
                _leaving++;
                break;
        }
    }

    public void EndApply(short ping, int queued)
    {
        _history.Add(new StateSample(_applying, _receivedBytes, _receivedBlocks, _entityCount, _entering, _leaving,
            ping, queued));

        LastAppliedTick = _applying;
        _receivedBytes = 0;

        // pruning every tick would be a dictionary walk per tick for nothing
        if (_applying.Value % EntityHistorySize == 0)
            PruneStale();
    }

    public void Reset()
    {
        _entities.Clear();
        LastAppliedTick = GameTick.Zero;
        AwaitingFull = false;
    }

    private void PruneStale()
    {
        foreach (var (netEnt, traffic) in _entities)
        {
            if (_applying.Value - traffic.LastUpdate.Value > EntityHistorySize)
                _stale.Add(netEnt);
        }

        foreach (var netEnt in _stale)
            _entities.Remove(netEnt);

        _stale.Clear();
    }
}
