using System;
using System.Collections.Generic;
using Engine.Shared.Debug.Diagnostics;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// Every per-frame render counter. Indexed by enum instead of by name so
/// bumping one from inside a draw loop is just an array increment.
/// </summary>
public enum RenderCounter
{
    BreakShader,
    BreakSampler,
    BreakUnshaded,
    BreakSpriteToShape,
    BreakShapeToSprite,
    SpriteBatches,
    ShapeBatches,
    TargetBinds,
    Clears,

    // GraphicsDevice.Metrics deltas sampled once a frame, spanning the same
    // window as everything else in this class (GameClient.Draw start to
    // end). MetricsDraw/Primitive/Sprite need no manual instrumentation at
    // all - they're the ground truth for "how many draw calls". MetricsTarget/
    // ClearDelta exist so ProfilerReport can cross-check them against
    // TargetBinds/Clears above and flag a render target/clear call that
    // bypasses GraphicsDeviceProfilerExtensions and would otherwise silently
    // show binds/f 0.0 forever.
    MetricsDrawDelta,
    MetricsPrimitiveDelta,
    MetricsSpriteDelta,
    MetricsTargetDelta,
    MetricsClearDelta,

    Count,
}

/// <summary>
/// Rolling stats for one counter.
/// </summary>
public readonly struct CounterStats
{
    public double Avg { get; }
    public double Max { get; }

    public CounterStats(double avg, double max)
    {
        Avg = avg;
        Max = max;
    }
}

/// <summary>
/// A render target the engine keeps alive, with how hard it gets used.
/// </summary>
public sealed class TargetInfo
{
    public string Name { get; }
    public int Width;
    public int Height;
    public SurfaceFormat Format;

    /// <summary>
    /// Lifetime count since the process started (or the last Reset) - never
    /// windowed, because a resize is rare enough that a rolling average of it
    /// would read as zero most of the time. Detected automatically in
    /// <see cref="RenderStats.TrackTarget"/> by comparing the target
    /// reference against the last one seen under this name, so a pass that
    /// starts reallocating every frame can't go unnoticed just because
    /// nobody added a manual counter for it.
    /// </summary>
    public int Reallocs;
    private RenderTarget2D? _lastSeen;

    // current-frame accumulators; committed into the *History buffers below
    // at EndFrame and cleared at BeginFrame, same pattern as everything else
    // in this file - reading these two fields directly gives a single
    // instant frame instead of the report's usual per-frame average.
    internal int Binds;
    internal int Clears;

    // CircularBuffer<T> is internal to Engine.Shared - these stay internal
    // too (Engine.Shared grants Engine.Client InternalsVisibleTo) rather than
    // public, since a public member can't expose a less-accessible type.
    internal CircularBuffer<double> BindsHistory { get; }
    internal CircularBuffer<double> ClearsHistory { get; }

    public long Bytes => (long)Width * Height * BytesPerPixel(Format);

    public TargetInfo(string name, int window)
    {
        Name = name;
        BindsHistory = new CircularBuffer<double>(window);
        ClearsHistory = new CircularBuffer<double>(window);
    }

    internal void Track(RenderTarget2D target)
    {
        if (_lastSeen is not null && !ReferenceEquals(_lastSeen, target))
            Reallocs++;

        _lastSeen = target;
        Width = target.Width;
        Height = target.Height;
        Format = target.Format;
    }

    private static int BytesPerPixel(SurfaceFormat format) => format switch
    {
        SurfaceFormat.Color => 4,
        SurfaceFormat.Single => 4,
        SurfaceFormat.HalfSingle => 2,
        SurfaceFormat.HalfVector4 => 8,
        SurfaceFormat.Vector4 => 16,
        _ => 4,
    };
}

/// <summary>
/// Collects everything the renderer does in a frame: batch breaks and why
/// they happened, render target usage, and an estimate of how many pixels
/// each lighting pass shades. Draw call/primitive totals come straight from
/// <see cref="GraphicsDevice.Metrics"/> instead of being hand-counted - see
/// <see cref="RenderCounter.MetricsTargetDelta"/>/<see cref="RenderCounter.MetricsClearDelta"/>.
///
/// Registered manually in GameClient's constructor (not [RegisterIoC]) and
/// early, alongside the other core rendering singletons - RenderManager
/// depends on this and is itself constructed before the engine's automatic
/// IoC scan for Engine.Client runs.
/// </summary>
public sealed class RenderStats
{
    public const int Window = 120;

    private readonly long[] _frame = new long[(int)RenderCounter.Count];
    private readonly CircularBuffer<double>[] _history = new CircularBuffer<double>[(int)RenderCounter.Count];

    // keyed by tuple so recording a transition doesn't build a string
    private readonly Dictionary<(string From, string To), int> _shaderTransitions = new();
    private readonly Dictionary<(string From, string To), int> _samplerTransitions = new();

    private readonly Dictionary<string, TargetInfo> _targets = new();
    private readonly List<string> _targetOrder = new();

    private readonly Dictionary<string, CircularBuffer<double>> _fill = new();
    private readonly List<string> _fillOrder = new();
    private readonly Dictionary<string, double> _fillFrame = new();

    public double SortMs { get; set; }
    private readonly CircularBuffer<double> _sortHistory = new(Window);

    /// <summary>
    /// Peak queue length seen since the last reset - not windowed, it is the
    /// worst case worth knowing about.
    /// </summary>
    public int QueuePeak { get; private set; }

    public RenderStats()
    {
        for (int i = 0; i < _history.Length; i++)
            _history[i] = new CircularBuffer<double>(Window);
    }

    public void Count(RenderCounter counter, long amount = 1)
        => _frame[(int)counter] += amount;

    public long Current(RenderCounter counter) => _frame[(int)counter];

    public CounterStats Get(RenderCounter counter)
    {
        var buffer = _history[(int)counter];
        return new CounterStats(buffer.Average(), buffer.Max());
    }

    public CounterStats SortStats => new(_sortHistory.Average(), _sortHistory.Max());

    public double AvgBatches =>
        Get(RenderCounter.SpriteBatches).Avg + Get(RenderCounter.ShapeBatches).Avg;

    public double AvgBatchBreaks =>
        Get(RenderCounter.BreakShader).Avg
        + Get(RenderCounter.BreakSampler).Avg
        + Get(RenderCounter.BreakUnshaded).Avg
        + Get(RenderCounter.BreakSpriteToShape).Avg
        + Get(RenderCounter.BreakShapeToSprite).Avg;

    public void BeginFrame()
    {
        Array.Clear(_frame);
        _fillFrame.Clear();
        SortMs = 0;

        foreach (var target in _targets.Values)
        {
            target.Binds = 0;
            target.Clears = 0;
        }
    }

    public void EndFrame()
    {
        for (int i = 0; i < _history.Length; i++)
            _history[i].Add(_frame[i]);

        _sortHistory.Add(SortMs);

        foreach (var target in _targets.Values)
        {
            target.BindsHistory.Add(target.Binds);
            target.ClearsHistory.Add(target.Clears);
        }

        foreach (var pass in _fillOrder)
            _fill[pass].Add(_fillFrame.TryGetValue(pass, out var px) ? px : 0);
    }

    /// <summary>
    /// Records how many entries were queued this frame, for <see cref="QueuePeak"/>.
    /// </summary>
    public void RecordQueueSize(int size)
    {
        if (size > QueuePeak)
            QueuePeak = size;
    }

    #region Batch breaks

    public void RecordShaderBreak(Effect? from, Effect? to)
    {
        Count(RenderCounter.BreakShader);
        Bump(_shaderTransitions, (ShaderLabel(from), ShaderLabel(to)));
    }

    public void RecordSamplerBreak(SamplerState? from, SamplerState? to)
    {
        Count(RenderCounter.BreakSampler);
        Bump(_samplerTransitions, (SamplerLabel(from), SamplerLabel(to)));
    }

    public IEnumerable<KeyValuePair<(string From, string To), int>> ShaderTransitions => _shaderTransitions;
    public IEnumerable<KeyValuePair<(string From, string To), int>> SamplerTransitions => _samplerTransitions;

    private static void Bump(Dictionary<(string, string), int> map, (string, string) key)
    {
        // a pathological frame shouldn't grow this without bound
        if (map.Count > 64 && !map.ContainsKey(key))
            return;

        map[key] = map.TryGetValue(key, out var n) ? n + 1 : 1;
    }

    /// <summary>
    /// Effects rarely carry a name, so fall back to the technique and then to
    /// an identity, which is still enough to tell two shaders apart.
    /// </summary>
    public static string ShaderLabel(Effect? effect)
    {
        if (effect is null)
            return "<default>";

        if (!string.IsNullOrEmpty(effect.Name))
            return effect.Name;

        var technique = effect.CurrentTechnique?.Name;
        return string.IsNullOrEmpty(technique)
            ? $"effect#{effect.GetHashCode():x}"
            : technique;
    }

    public static string SamplerLabel(SamplerState? sampler)
    {
        if (sampler is null)
            return "<default>";
        if (sampler == SamplerState.PointClamp) return "PointClamp";
        if (sampler == SamplerState.PointWrap) return "PointWrap";
        if (sampler == SamplerState.LinearClamp) return "LinearClamp";
        if (sampler == SamplerState.LinearWrap) return "LinearWrap";
        if (sampler == SamplerState.AnisotropicClamp) return "AnisotropicClamp";
        if (sampler == SamplerState.AnisotropicWrap) return "AnisotropicWrap";
        return string.IsNullOrEmpty(sampler.Name) ? "custom" : sampler.Name;
    }

    #endregion

    #region Render targets

    /// <summary>
    /// Registers or refreshes a target in the inventory and, comparing it
    /// against the reference last seen under this name, detects a reallocation
    /// automatically. Called by <see cref="GraphicsDeviceProfilerExtensions.SetRenderTargetTracked"/>
    /// so every bound target is tracked by construction.
    /// </summary>
    public void TrackTarget(string name, RenderTarget2D? target)
    {
        if (target is null || target.IsDisposed)
            return;

        GetTarget(name).Track(target);
    }

    public void RecordBind(string name)
    {
        Count(RenderCounter.TargetBinds);
        GetTarget(name).Binds++;
    }

    public void RecordClear(string name)
    {
        Count(RenderCounter.Clears);
        GetTarget(name).Clears++;
    }

    private TargetInfo GetTarget(string name)
    {
        if (_targets.TryGetValue(name, out var info))
            return info;

        info = new TargetInfo(name, Window);
        _targets[name] = info;
        _targetOrder.Add(name);
        return info;
    }

    public IEnumerable<TargetInfo> Targets
    {
        get
        {
            foreach (var name in _targetOrder)
                yield return _targets[name];
        }
    }

    #endregion

    #region Fill rate

    /// <summary>
    /// Records how many pixels a pass shades this frame. In a 2D engine with
    /// dynamic lighting this is usually what the GPU is actually spending its
    /// time on, so it is estimated analytically rather than measured.
    /// </summary>
    public void AddFill(string pass, double pixels)
    {
        if (!_fill.ContainsKey(pass))
        {
            _fill[pass] = new CircularBuffer<double>(Window);
            _fillOrder.Add(pass);
        }

        _fillFrame[pass] = _fillFrame.TryGetValue(pass, out var current) ? current + pixels : pixels;
    }

    public IEnumerable<(string Pass, double AvgPixels, double MaxPixels)> Fill
    {
        get
        {
            foreach (var pass in _fillOrder)
            {
                var buffer = _fill[pass];
                yield return (pass, buffer.Average(), buffer.Max());
            }
        }
    }

    public double TotalAvgFill
    {
        get
        {
            double total = 0;
            foreach (var pass in _fillOrder)
                total += _fill[pass].Average();
            return total;
        }
    }

    #endregion

    public void Reset()
    {
        Array.Clear(_frame);
        for (int i = 0; i < _history.Length; i++)
            _history[i].Clear();

        _shaderTransitions.Clear();
        _samplerTransitions.Clear();
        _targets.Clear();
        _targetOrder.Clear();
        _fill.Clear();
        _fillOrder.Clear();
        _fillFrame.Clear();
        _sortHistory.Clear();
        QueuePeak = 0;
    }
}
