using System;
using System.Collections.Generic;
using System.Diagnostics;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.IoC;

namespace Engine.Shared.Debug.Diagnostics;

/// <summary>
/// One timed region of a frame. Nesting is encoded in <see cref="Depth"/> - the
/// frame keeps its samples flat and the tree is rebuilt from the depth when
/// reporting, so no tree objects get allocated per frame.
/// </summary>
public readonly struct ScopeSample
{
    public string Name { get; }
    public int Depth { get; }
    public double Ms { get; }
    public long AllocBytes { get; }

    public ScopeSample(string name, int depth, double ms, long allocBytes)
    {
        Name = name;
        Depth = depth;
        Ms = ms;
        AllocBytes = allocBytes;
    }
}

/// <summary>
/// A scope's timings aggregated over the rolling window.
/// </summary>
public readonly struct ScopeStats
{
    public string Name { get; }
    public int Depth { get; }
    public double AvgMs { get; }
    public double MaxMs { get; }
    public double MinMs { get; }
    public double AvgAllocBytes { get; }
    public double AvgCalls { get; }

    public ScopeStats(string name, int depth, double avgMs, double maxMs, double minMs,
        double avgAllocBytes, double avgCalls)
    {
        Name = name;
        Depth = depth;
        AvgMs = avgMs;
        MaxMs = maxMs;
        MinMs = minMs;
        AvgAllocBytes = avgAllocBytes;
        AvgCalls = avgCalls;
    }
}

/// <summary>
/// Hierarchical per-frame timer. Wrap a region with
/// <c>using var _ = profiler.Scope("name")</c>; scopes nest freely and get
/// aggregated over a rolling window of frames.
/// </summary>
[RegisterIoC]
public sealed class FrameProfiler
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    /// <summary>
    /// Set by the client side to force the GPU pipeline to drain. Lets
    /// <see cref="GpuScope"/> measure real GPU cost without this assembly
    /// knowing anything about the graphics backend.
    /// </summary>
    public Action? GpuFlush { get; set; }

    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// Whether GPU passes force a pipeline flush so their scope measures GPU
    /// work instead of just command submission. Distorts absolute frame time.
    /// </summary>
    public bool GpuSyncEnabled { get; private set; }

    public int WindowSize { get; private set; } = 120;

    // scopes of the frame being built, flat, reused across frames
    private readonly List<Pending> _building = new(64);
    private List<ScopeSample> _lastFrame = new(64);
    private List<ScopeSample> _lastFrameSpare = new(64);

    // indices into _building of the currently open scopes
    private readonly Stack<int> _open = new();

    private readonly Dictionary<string, Entry> _entries = new(64);
    private readonly List<string> _order = new(64);

    private CircularBuffer<double> _frameMs = new(120);
    private CircularBuffer<double> _presentMs = new(120);
    private double[] _scratch = new double[120];

    private long _frameStart;
    private bool _inFrame;

    public FrameProfiler()
        => IoCManager.ResolveDependencies(this);

    /// <summary>
    /// Samples of the most recently completed frame, in submission order.
    /// </summary>
    public IReadOnlyList<ScopeSample> LastFrameSamples => _lastFrame;

    public double FrameAvgMs => _frameMs.Average();
    public double FrameMaxMs => _frameMs.Max();
    public double FrameMinMs => _frameMs.Min();
    public double FramePercentileMs(double p) => _frameMs.Percentile(p, _scratch);
    public double PresentAvgMs => _presentMs.Average();
    public double PresentMaxMs => _presentMs.Max();
    public int SampledFrames => _frameMs.Count;

    /// <summary>
    /// Opens a timed scope. Dispose it (or let <c>using</c> do it) to close.
    /// </summary>
    public ProfilerScope Scope(string name) => OpenScope(name, false);

    /// <summary>
    /// Same as <see cref="Scope"/>, but when gpu-sync is on the pipeline is
    /// drained before the timer stops, so the sample is the pass' GPU cost.
    /// </summary>
    public ProfilerScope GpuScope(string name) => OpenScope(name, true);

    private ProfilerScope OpenScope(string name, bool gpu)
    {
        if (!Enabled || !_inFrame)
            return default;

        var index = _building.Count;
        _building.Add(new Pending
        {
            Name = name,
            Depth = _open.Count,
            StartTicks = Stopwatch.GetTimestamp(),
            StartAlloc = GC.GetAllocatedBytesForCurrentThread(),
            Gpu = gpu,
        });
        _open.Push(index);

        return new ProfilerScope(this, index);
    }

    internal void CloseScope(int index)
    {
        if (index < 0 || index >= _building.Count)
            return;

        var pending = _building[index];
        if (pending.Closed)
            return;

        // a gpu pass' commands are still queued at this point - draining first
        // makes the elapsed time include the work the driver actually did
        if (pending.Gpu && GpuSyncEnabled)
            GpuFlush?.Invoke();

        pending.Ticks = Stopwatch.GetTimestamp() - pending.StartTicks;
        pending.AllocBytes = GC.GetAllocatedBytesForCurrentThread() - pending.StartAlloc;
        pending.Closed = true;
        _building[index] = pending;

        // scopes close LIFO, but a stray mismatch shouldn't corrupt the depth
        while (_open.Count > 0 && _building[_open.Peek()].Closed)
            _open.Pop();
    }

    /// <summary>
    /// Starts a new frame. Safe to call more than once per rendered frame -
    /// MonoGame runs several Updates per Draw while catching up on a fixed
    /// timestep, and those accumulate into the same frame.
    /// </summary>
    public void BeginFrame()
    {
        RefreshSettings();

        if (!Enabled || _inFrame)
            return;

        _inFrame = true;
        _frameStart = Stopwatch.GetTimestamp();
        _building.Clear();
        _open.Clear();
    }

    /// <summary>
    /// Closes the frame and folds its samples into the rolling window.
    /// </summary>
    public void EndFrame()
    {
        if (!Enabled || !_inFrame)
            return;

        _inFrame = false;
        _frameMs.Add(TicksToMs(Stopwatch.GetTimestamp() - _frameStart));

        // publish into the spare list then swap - the reader gets a stable
        // list and nothing allocates
        _lastFrameSpare.Clear();
        foreach (var p in _building)
            _lastFrameSpare.Add(new ScopeSample(p.Name, p.Depth, TicksToMs(p.Ticks), p.AllocBytes));

        (_lastFrame, _lastFrameSpare) = (_lastFrameSpare, _lastFrame);

        Aggregate();
    }

    /// <summary>
    /// Time spent outside Update/Draw - Present, vsync wait, and whatever else
    /// the driver blocks on between frames.
    /// </summary>
    public void RecordPresentWait(double ms)
    {
        if (Enabled)
            _presentMs.Add(ms);
    }

    private void Aggregate()
    {
        // same-named scopes hit more than once in a frame collapse into one
        // window sample, so the average stays "per frame" not "per call"
        foreach (var entry in _entries.Values)
            entry.ResetFrame();

        foreach (var p in _building)
        {
            if (!_entries.TryGetValue(p.Name, out var entry))
            {
                entry = new Entry(p.Name, WindowSize);
                _entries[p.Name] = entry;
                _order.Add(p.Name);
            }

            entry.Depth = p.Depth;
            entry.FrameMs += TicksToMs(p.Ticks);
            entry.FrameAlloc += p.AllocBytes;
            entry.FrameCalls++;
        }

        foreach (var entry in _entries.Values)
            entry.CommitFrame();
    }

    /// <summary>
    /// Aggregated stats for every scope seen, in first-seen order so the
    /// hierarchy reads top to bottom.
    /// </summary>
    public List<ScopeStats> GetStats()
    {
        var result = new List<ScopeStats>(_order.Count);
        foreach (var name in _order)
        {
            if (!_entries.TryGetValue(name, out var e))
                continue;

            result.Add(new ScopeStats(
                e.Name,
                e.Depth,
                e.Ms.Average(),
                e.Ms.Max(),
                e.Ms.Min(),
                e.Alloc.Average(),
                e.Calls.Average()));
        }
        return result;
    }

    /// <summary>
    /// Clears the rolling windows. Does not touch <see cref="Enabled"/>/<see cref="GpuSyncEnabled"/>.
    /// </summary>
    public void Reset()
    {
        _entries.Clear();
        _order.Clear();
        _frameMs.Clear();
        _presentMs.Clear();
        _building.Clear();
        _open.Clear();
        _inFrame = false;
    }

    private void RefreshSettings()
    {
        Enabled = _cfg.Get(ProfilerCvars.Enabled);
        GpuSyncEnabled = _cfg.Get(ProfilerCvars.GpuSync);

        var window = Math.Max(2, _cfg.Get(ProfilerCvars.Window));
        if (window == WindowSize)
            return;

        WindowSize = window;
        _frameMs = new CircularBuffer<double>(window);
        _presentMs = new CircularBuffer<double>(window);
        _scratch = new double[window];
        _entries.Clear();
        _order.Clear();
    }

    internal static double TicksToMs(long ticks)
        => ticks * 1000.0 / Stopwatch.Frequency;

    private struct Pending
    {
        public string Name;
        public int Depth;
        public long StartTicks;
        public long StartAlloc;
        public long Ticks;
        public long AllocBytes;
        public bool Gpu;
        public bool Closed;
    }

    private sealed class Entry
    {
        public string Name { get; }
        public int Depth;
        public CircularBuffer<double> Ms { get; }
        public CircularBuffer<double> Alloc { get; }
        public CircularBuffer<double> Calls { get; }

        public double FrameMs;
        public long FrameAlloc;
        public int FrameCalls;

        public Entry(string name, int window)
        {
            Name = name;
            Ms = new CircularBuffer<double>(window);
            Alloc = new CircularBuffer<double>(window);
            Calls = new CircularBuffer<double>(window);
        }

        public void ResetFrame()
        {
            FrameMs = 0;
            FrameAlloc = 0;
            FrameCalls = 0;
        }

        public void CommitFrame()
        {
            Ms.Add(FrameMs);
            Alloc.Add(FrameAlloc);
            Calls.Add(FrameCalls);
        }
    }
}

/// <summary>
/// Handle returned by <see cref="FrameProfiler.Scope"/>. Disposing closes the
/// scope; a default instance (profiler off) does nothing.
/// </summary>
public readonly struct ProfilerScope : IDisposable
{
    private readonly FrameProfiler? _profiler;
    private readonly int _index;

    internal ProfilerScope(FrameProfiler profiler, int index)
    {
        _profiler = profiler;
        _index = index;
    }

    public void Dispose() => _profiler?.CloseScope(_index);
}
