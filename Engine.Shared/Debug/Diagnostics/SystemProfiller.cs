using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.IoC;

namespace Engine.Shared.Debug.Diagnostics;

/// <summary>
/// Records per-system CPU timing and exposes aggregated statistics
/// for the debug overlay. Thread-safe for reads; writes must happen
/// on the update thread.
/// </summary>
[RegisterIoC]
public sealed class SystemsProfiler
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    /// <summary>
    /// Number of frames kept per system for the rolling average.
    /// </summary>
    public const int RollingWindowSize = 120;
    private readonly Dictionary<string, SystemEntry> _entries = new(32);

    public SystemsProfiler()
        => IoCManager.ResolveDependencies(this);

    /// <summary>
    /// Record a system's Update() for this frame, in milliseconds.
    /// </summary>
    public void RecordUpdate(string systemName, double updateMs, long allocBytes = 0)
    {
        var entry = GetEntry(systemName);
        entry.UpdateSamples.Add(updateMs);
        entry.UpdateAllocSamples.Add(allocBytes);
    }

    /// <summary>
    /// Record a system's Draw() for this frame, in milliseconds, along with how
    /// many renderables it submitted.
    /// </summary>
    public void RecordDraw(string systemName, double drawMs, long allocBytes = 0, int submits = 0)
    {
        var entry = GetEntry(systemName);
        entry.DrawSamples.Add(drawMs);
        entry.DrawAllocSamples.Add(allocBytes);
        entry.SubmitSamples.Add(submits);
    }

    private SystemEntry GetEntry(string systemName)
    {
        if (_entries.TryGetValue(systemName, out var entry))
            return entry;

        entry = new SystemEntry(systemName, RollingWindowSize);
        _entries[systemName] = entry;
        return entry;
    }

    /// <summary>
    /// Returns the top systems ordered by their
    /// combined (Update + Draw) rolling average, descending
    /// </summary>
    public List<SystemSnapshot> GetTop(int count = 0)
    {
        if (count == 0)
            count = _cfg.Get(EngineCvars.SystemProfillerTop);

        return GetAll()
            .OrderByDescending(s => s.TotalMs)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Returns a snapshot for every tracked system (unordered).
    /// </summary>
    public IEnumerable<SystemSnapshot> GetAll()
        => _entries.Values.Select(e => new SystemSnapshot(
               e.Name,
               e.UpdateSamples.Average(),
               e.DrawSamples.Average(),
               e.UpdateSamples.Max(),
               e.DrawSamples.Max(),
               e.UpdateAllocSamples.Average() + e.DrawAllocSamples.Average(),
               e.SubmitSamples.Average()));

    /// <summary>
    /// Clears all recorded data
    /// </summary>
    public void Reset() => _entries.Clear();

    private sealed class SystemEntry
    {
        public string Name { get; }
        public CircularBuffer<double> UpdateSamples { get; }
        public CircularBuffer<double> DrawSamples { get; }
        public CircularBuffer<double> UpdateAllocSamples { get; }
        public CircularBuffer<double> DrawAllocSamples { get; }
        public CircularBuffer<double> SubmitSamples { get; }

        public SystemEntry(string name, int windowSize)
        {
            Name = name;
            UpdateSamples = new CircularBuffer<double>(windowSize);
            DrawSamples = new CircularBuffer<double>(windowSize);
            UpdateAllocSamples = new CircularBuffer<double>(windowSize);
            DrawAllocSamples = new CircularBuffer<double>(windowSize);
            SubmitSamples = new CircularBuffer<double>(windowSize);
        }
    }
}

/// <summary>
/// Immutable snapshot of a single system timing.
/// </summary>
public readonly struct SystemSnapshot
{
    public string Name { get; }

    public double UpdateMs { get; }
    public double DrawMs { get; }
    public double UpdateMaxMs { get; }
    public double DrawMaxMs { get; }

    /// <summary>
    /// Bytes allocated per frame inside this system's Update and Draw.
    /// </summary>
    public double AllocBytes { get; }

    /// <summary>
    /// Renderables this system pushed into the render queue per frame.
    /// </summary>
    public double Submits { get; }

    /// <summary>
    /// Combined Update + Draw time in ms.
    /// </summary>
    public double TotalMs => UpdateMs + DrawMs;

    public SystemSnapshot(string name, double updateMs, double drawMs,
        double updateMaxMs = 0, double drawMaxMs = 0, double allocBytes = 0, double submits = 0)
    {
        Name = name;
        UpdateMs = updateMs;
        DrawMs = drawMs;
        UpdateMaxMs = updateMaxMs;
        DrawMaxMs = drawMaxMs;
        AllocBytes = allocBytes;
        Submits = submits;
    }
}
