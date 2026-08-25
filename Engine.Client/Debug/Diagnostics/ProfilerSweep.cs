using System;
using System.Collections.Generic;
using System.Diagnostics;
using Engine.Client.Graphics.Lighting;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.IoC;

namespace Engine.Client.Debug.Diagnostics;

/// <summary>
/// What one sweep configuration cost, against the baseline.
/// </summary>
public readonly struct SweepResult
{
    public string Name { get; }
    public double MedianMs { get; }

    /// <summary>
    /// Milliseconds the baseline spends that this configuration does not.
    /// Effectively the end to end cost of whatever was switched off. Zero
    /// for the baseline itself.
    /// </summary>
    public double SavedMs { get; }

    /// <summary>
    /// Max minus min sample for this configuration, across every round.
    /// A large spread relative to SavedMs means the result is noise, not a
    /// real difference - see ProfilerReport's contamination warning.
    /// </summary>
    public double SpreadMs { get; }

    public SweepResult(string name, double medianMs, double savedMs, double spreadMs)
    {
        Name = name;
        MedianMs = medianMs;
        SavedMs = savedMs;
        SpreadMs = spreadMs;
    }
}

/// <summary>
/// Measures the real cost of each render feature by turning it off and timing
/// the difference. Needs no GPU timer query, and unlike a CPU scope it catches
/// work the driver does asynchronously.
///
/// Every configuration is measured across several short, interleaved rounds
/// instead of one long contiguous block: round 0 measures baseline, lighting
/// off, shadows off, ... in order, round 1 measures the same list back to
/// front, and so on. A monotonic drift over the sweep's runtime (clocks on a
/// shared CPU/GPU power budget ramping as later configurations happen to run
/// later in wall-clock time, a hitch, GC pressure) then lands on every
/// configuration roughly equally instead of piling entirely onto whichever
/// one was measured last - the failure mode a single contiguous block per
/// configuration has no way to tell apart from a real difference.
/// </summary>
[RegisterIoC]
public sealed class ProfilerSweep
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly LightingManager _lighting = default!;

    private const int WarmupFrames = 4;

    // disabling vsync itself is a bigger transition than toggling a lighting
    // flag - give the very first segment (baseline, round 0) extra frames to
    // settle so it isn't measured artificially fast against everything after it
    private const int FirstSegmentWarmupFrames = WarmupFrames * 3;

    /// <summary>
    /// Set while a configuration asks for the UI or the world to be skipped.
    /// Read by GameClient.Draw.
    /// </summary>
    public static bool SkipUi { get; private set; }
    public static bool SkipWorld { get; private set; }

    public bool Running { get; private set; }

    /// <summary>
    /// Results of the last completed sweep, in declared configuration order
    /// (baseline first) - not sorted by cost, so the order stays the same
    /// list every time and a reader can compare run to run directly.
    /// </summary>
    public IReadOnlyList<SweepResult> Results => _results;

    private readonly List<SweepResult> _results = new();
    private readonly List<Config> _configs = new();
    private List<double>[] _samplesByConfig = Array.Empty<List<double>>();

    private int _round;
    private int _pos;
    private int _frameInSegment;
    private int _frames = 20;
    private int _rounds = 5;
    private long _lastFrameStamp;

    // saved so the game is left exactly as it was found
    private bool _savedLighting;
    private bool _savedWallBleed;
    private bool _savedLightBlur;
    private int _savedShadowLights;
    private float _savedLightmapScale;
    private bool _savedVsync;
    private bool _savedFixedTimestep;

    public ProfilerSweep()
        => IoCManager.ResolveDependencies(this);

    /// <summary>
    /// Starts a sweep. Takes a few seconds, during which the game runs with
    /// vsync off and features cycling on and off.
    /// </summary>
    public void Start()
    {
        if (Running)
            return;

        _frames = Math.Max(5, _cfg.Get(ProfilerCvars.SweepFramesPerRound));
        _rounds = Math.Max(2, _cfg.Get(ProfilerCvars.SweepRounds));

        SaveState();
        BuildConfigs();

        _samplesByConfig = new List<double>[_configs.Count];
        for (int i = 0; i < _configs.Count; i++)
            _samplesByConfig[i] = new List<double>(_rounds * _frames);

        // vsync pins every configuration to the refresh rate, which would make
        // the whole sweep read as "no difference"
        GameClient.Graphics.SynchronizeWithVerticalRetrace = false;
        GameClient.Instance.IsFixedTimeStep = false;
        GameClient.Graphics.ApplyChanges();

        _results.Clear();
        _round = 0;
        _pos = 0;
        _frameInSegment = 0;
        _lastFrameStamp = 0;
        Running = true;

        Apply(_configs[ConfigIndexAt(0, 0)]);
        Log.Debug($"Profiler sweep started: {_configs.Count} configs x {_rounds} rounds x {_frames} frames");
    }

    /// <summary>
    /// Advances the sweep by one frame. Called at the end of every Draw.
    /// </summary>
    public void Tick()
    {
        if (!Running)
            return;

        var now = Stopwatch.GetTimestamp();
        var previous = _lastFrameStamp;
        _lastFrameStamp = now;

        if (previous == 0)
            return; // first frame after a config switch - the switch itself polluted this delta

        _frameInSegment++;

        var warmup = (_round == 0 && _pos == 0) ? FirstSegmentWarmupFrames : WarmupFrames;
        if (_frameInSegment <= warmup)
            return;

        var configIndex = ConfigIndexAt(_round, _pos);
        _samplesByConfig[configIndex].Add((now - previous) * 1000.0 / Stopwatch.Frequency);

        if (_frameInSegment < warmup + _frames)
            return;

        AdvanceSegment();
    }

    // even rounds measure configs 0..N-1, odd rounds N-1..0 - alternating
    // direction so a drift trend doesn't correlate with position-in-round either
    private int ConfigIndexAt(int round, int pos)
        => (round % 2 == 0) ? pos : _configs.Count - 1 - pos;

    private void AdvanceSegment()
    {
        Restore();

        _pos++;
        if (_pos >= _configs.Count)
        {
            _pos = 0;
            _round++;
        }

        _frameInSegment = 0;
        _lastFrameStamp = 0;

        if (_round >= _rounds)
        {
            Finish();
            return;
        }

        Apply(_configs[ConfigIndexAt(_round, _pos)]);
    }

    private void Finish()
    {
        GameClient.Graphics.SynchronizeWithVerticalRetrace = _savedVsync;
        GameClient.Instance.IsFixedTimeStep = _savedFixedTimestep;
        GameClient.Graphics.ApplyChanges();

        BuildResults();
        Running = false;
        Log.Debug("Profiler sweep finished");
    }

    private void BuildResults()
    {
        _results.Clear();

        double baselineMedian = 0;
        for (int i = 0; i < _configs.Count; i++)
        {
            var samples = _samplesByConfig[i];
            var median = Median(samples);
            var spread = Spread(samples);

            if (i == 0)
                baselineMedian = median;

            _results.Add(new SweepResult(_configs[i].Name, median, i == 0 ? 0 : baselineMedian - median, spread));
        }
    }

    private static double Median(List<double> samples)
    {
        if (samples.Count == 0)
            return 0;

        // BuildResults runs once per completed sweep, not per frame - fine to allocate
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static double Spread(List<double> samples)
    {
        if (samples.Count == 0)
            return 0;

        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in samples)
        {
            if (s < min) min = s;
            if (s > max) max = s;
        }
        return max - min;
    }

    private void BuildConfigs()
    {
        _configs.Clear();
        _configs.Add(new Config("baseline", () => { }));

        if (_savedLighting)
        {
            _configs.Add(new Config("lighting off", () => _lighting.SetEnabled(false)));
            _configs.Add(new Config("shadows off", () => _lighting.MaxShadowcastingLights = 1));
            _configs.Add(new Config("lightmap at 1/4", () => _lighting.LightmapScale = Math.Max(0.1f, _savedLightmapScale / 2f)));

            if (_savedWallBleed)
                _configs.Add(new Config("wall bleed off", () => _lighting.WallBleedEnabled = false));

            if (_savedLightBlur)
                _configs.Add(new Config("light blur off", () => _lighting.LightBlurEnabled = false));
        }

        _configs.Add(new Config("ui off", () => SkipUi = true));
        _configs.Add(new Config("world off", () => SkipWorld = true));
    }

    private static void Apply(Config config) => config.Apply();

    private void SaveState()
    {
        _savedLighting = _lighting.Enabled;
        _savedWallBleed = _lighting.WallBleedEnabled;
        _savedLightBlur = _lighting.LightBlurEnabled;
        _savedShadowLights = _lighting.MaxShadowcastingLights;
        _savedLightmapScale = _lighting.LightmapScale;
        _savedVsync = GameClient.Graphics.SynchronizeWithVerticalRetrace;
        _savedFixedTimestep = GameClient.Instance.IsFixedTimeStep;
    }

    private void Restore()
    {
        _lighting.SetEnabled(_savedLighting);
        _lighting.WallBleedEnabled = _savedWallBleed;
        _lighting.LightBlurEnabled = _savedLightBlur;
        _lighting.MaxShadowcastingLights = _savedShadowLights;
        _lighting.LightmapScale = _savedLightmapScale;
        SkipUi = false;
        SkipWorld = false;
    }

    private readonly struct Config
    {
        public string Name { get; }
        public Action Apply { get; }

        public Config(string name, Action apply)
        {
            Name = name;
            Apply = apply;
        }
    }
}
