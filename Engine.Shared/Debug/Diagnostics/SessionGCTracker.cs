using System;
using Engine.Shared.IoC;

namespace Engine.Shared.Debug.Diagnostics;

/// <summary>
/// GC.CollectionCount only reports a running total, not "since when" - to get
/// an interval you have to sample it yourself at both ends and subtract. This
/// keeps two baselines: one from when the process started, one from the last
/// checkpoint, so a report can show both "since boot" and "since the last
/// dump" instead of a delta reset every Update() call, which is almost always
/// zero because collections don't happen every frame.
/// </summary>
[RegisterIoC]
public sealed class SessionGCTracker
{
    private readonly int _startGen0, _startGen1, _startGen2;
    private readonly long _startBytes;

    private int _checkpointGen0, _checkpointGen1, _checkpointGen2;
    private long _checkpointBytes;

    public SessionGCTracker()
    {
        _startGen0 = GC.CollectionCount(0);
        _startGen1 = GC.CollectionCount(1);
        _startGen2 = GC.CollectionCount(2);
        _startBytes = GC.GetAllocatedBytesForCurrentThread();

        _checkpointGen0 = _startGen0;
        _checkpointGen1 = _startGen1;
        _checkpointGen2 = _startGen2;
        _checkpointBytes = _startBytes;
    }

    public GCDelta SinceStart => new(
        GC.CollectionCount(0) - _startGen0,
        GC.CollectionCount(1) - _startGen1,
        GC.CollectionCount(2) - _startGen2,
        GC.GetAllocatedBytesForCurrentThread() - _startBytes);

    public GCDelta SinceCheckpoint => new(
        GC.CollectionCount(0) - _checkpointGen0,
        GC.CollectionCount(1) - _checkpointGen1,
        GC.CollectionCount(2) - _checkpointGen2,
        GC.GetAllocatedBytesForCurrentThread() - _checkpointBytes);

    /// <summary>
    /// Resets the "since checkpoint" baseline to now. Call after reading
    /// <see cref="SinceCheckpoint"/> for a report, so the next one shows what
    /// happened since this one instead of since boot.
    /// </summary>
    public void MarkCheckpoint()
    {
        _checkpointGen0 = GC.CollectionCount(0);
        _checkpointGen1 = GC.CollectionCount(1);
        _checkpointGen2 = GC.CollectionCount(2);
        _checkpointBytes = GC.GetAllocatedBytesForCurrentThread();
    }
}

public readonly struct GCDelta
{
    public int Gen0 { get; }
    public int Gen1 { get; }
    public int Gen2 { get; }
    public long AllocatedBytes { get; }

    public GCDelta(int gen0, int gen1, int gen2, long allocatedBytes)
    {
        Gen0 = gen0;
        Gen1 = gen1;
        Gen2 = gen2;
        AllocatedBytes = allocatedBytes;
    }
}
