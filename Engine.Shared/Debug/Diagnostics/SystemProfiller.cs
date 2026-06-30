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
    public const int RollingWindowSize = 60;
    private readonly Dictionary<string, SystemEntry> _entries = new(32);

    public SystemsProfiler()
        => IoCManager.ResolveDependencies(this);

    /// <summary>
    /// Record a single frame sample for a system (in milliseconds).
    /// Called by EntityManager after timing each system's Update() and Draw().
    /// </summary>
    public void Record(string systemName, double updateMs, double drawMs)
    {
        if (!_entries.TryGetValue(systemName, out var entry))
        {
            entry = new SystemEntry(systemName, RollingWindowSize);
            _entries[systemName] = entry;
        }

        entry.UpdateSamples.Add(updateMs);
        entry.DrawSamples.Add(drawMs);
    }

    /// <summary>
    /// Returns the top systems ordered by their
    /// combined (Update + Draw) rolling average, descending
    /// </summary>
    public List<SystemSnapshot> GetTop(int count = 0)
    {
        if (count == 0)
            count = _cfg.Get(EngineCvars.SystemProfillerTop);
        
        return _entries.Values
            .Select(e => new SystemSnapshot(
                e.Name,
                e.UpdateSamples.Average(),
                e.DrawSamples.Average()))
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
               e.DrawSamples.Average()));

    /// <summary>
    /// Clears all recorded data
    /// </summary>
    public void Reset() => _entries.Clear();

    private sealed class SystemEntry
    {
        public string Name { get; }
        public CircularBuffer<double> UpdateSamples { get; }
        public CircularBuffer<double> DrawSamples { get; }

        public SystemEntry(string name, int windowSize)
        {
            Name = name;
            UpdateSamples = new CircularBuffer<double>(windowSize);
            DrawSamples = new CircularBuffer<double>(windowSize);
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

    /// <summary>
    /// Combined Update + Draw time in ms.
    /// </summary>
    public double TotalMs => UpdateMs + DrawMs;

    public SystemSnapshot(string name, double updateMs, double drawMs)
    {
        Name = name;
        UpdateMs = updateMs;
        DrawMs = drawMs;
    }
}
