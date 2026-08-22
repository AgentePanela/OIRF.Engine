using System;
using System.Collections.Generic;
using System.Diagnostics;
using Engine.Client.Graphics.Lighting;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.Debug.Diagnostics;
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
    /// Effectively the end to end cost of whatever was switched off.
    /// </summary>
    public double SavedMs { get; }

    public SweepResult(string name, double medianMs, double savedMs)
    {
        Name = name;
        MedianMs = medianMs;
        SavedMs = savedMs;
    }
}

/// <summary>
/// Measures the real cost of each render feature by turning it off and timing
/// the difference. Needs no GPU timer query, and unlike a CPU scope it catches
/// work the driver does asynchronously.
/// </summary>
[RegisterIoC]
public sealed class ProfilerSweep
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly LightingManager _lighting = default!;

    private const int WarmupFrames = 8;

    /// <summary>
    /// Set while a configuration asks for the UI or the world to be skipped.
    /// Read by GameClient.Draw.
    /// </summary>
    public static bool SkipUi { get; private set; }
    public static bool SkipWorld { get; private set; }

    public bool Running { get; private set; }

    /// <summary>
    /// Results of the last completed sweep, ordered by how much each feature
    /// costs, most expensive first.
    /// </summary>
    public IReadOnlyList<SweepResult> Results => _results;

    private readonly List<SweepResult> _results = new();
    private readonly List<Config> _configs = new();
    private CircularBuffer<double> _samples = new(60);
    private double[] _scratch = new double[60];

    private int _configIndex;
    private int _frameIndex;
    private int _frames = 60;
    private double _baselineMs;
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
    /// Starts a sweep. Takes a couple of seconds, during which the game runs
    /// with features switched off and vsync disabled.
    /// </summary>
    public void Start()
    {
        if (Running)
            return;

        _frames = Math.Max(10, _cfg.Get(EngineCvars.ProfilerSweepFrames));
        _samples = new CircularBuffer<double>(_frames);
        _scratch = new double[_frames];

        SaveState();
        BuildConfigs();

        // vsync pins every configuration to the refresh rate, which would make
        // the whole sweep read as "no difference"
        GameClient.Graphics.SynchronizeWithVerticalRetrace = false;
        GameClient.Instance.IsFixedTimeStep = false;
        GameClient.Graphics.ApplyChanges();

        _results.Clear();
        _configIndex = 0;
        _frameIndex = 0;
        _baselineMs = 0;
        _lastFrameStamp = 0;
        Running = true;

        Apply(_configs[0]);
        Log.Debug($"Profiler sweep started: {_configs.Count} configs x {_frames} frames");
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
            return;

        _frameIndex++;

        // let the driver settle after a config change before believing numbers
        if (_frameIndex <= WarmupFrames)
            return;

        _samples.Add((now - previous) * 1000.0 / Stopwatch.Frequency);

        if (_samples.Count < _frames)
            return;

        FinishConfig();
    }

    private void FinishConfig()
    {
        var config = _configs[_configIndex];

        // median, because one hitch would wreck an average over 60 frames
        var median = _samples.Median(_scratch);

        if (_configIndex == 0)
            _baselineMs = median;

        _results.Add(new SweepResult(config.Name, median, _baselineMs - median));

        Restore();
        _configIndex++;
        _frameIndex = 0;
        _lastFrameStamp = 0;
        _samples.Clear();

        if (_configIndex >= _configs.Count)
        {
            Stop();
            return;
        }

        Apply(_configs[_configIndex]);
    }

    private void Stop()
    {
        Restore();
        GameClient.Graphics.SynchronizeWithVerticalRetrace = _savedVsync;
        GameClient.Instance.IsFixedTimeStep = _savedFixedTimestep;
        GameClient.Graphics.ApplyChanges();

        // baseline first, then whatever costs most
        _results.Sort((a, b) => b.SavedMs.CompareTo(a.SavedMs));
        Running = false;
        Log.Debug("Profiler sweep finished");
    }

    private void BuildConfigs()
    {
        _configs.Clear();
        _configs.Add(new Config("baseline", () => { }));

        if (_savedLighting)
        {
            _configs.Add(new Config("lighting off", () => _lighting.SetEnabled(false)));
            _configs.Add(new Config("shadows off", () => _lighting.MaxShadowcastingLights = 0));
            _configs.Add(new Config("lightmap at 1/4", () => _lighting.LightmapScale = _savedLightmapScale / 2f));

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
