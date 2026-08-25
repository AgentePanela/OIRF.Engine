namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public sealed class ProfilerCvars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("profiler.enabled", true);

    /// <summary>
    /// Frames kept for the rolling averages in <see cref="Debug.Diagnostics.FrameProfiler"/>.
    /// </summary>
    public static readonly CVarDef<int> Window =
        CVarDef.Create("profiler.window", 120);

    /// <summary>
    /// Drains the GPU pipeline after each GPU-timed scope so its timer measures
    /// real GPU cost instead of just command submission. Inflates total frame
    /// time on purpose - only meant for comparing passes against each other,
    /// never as a normal frame time.
    /// </summary>
    public static readonly CVarDef<bool> GpuSync =
        CVarDef.Create("profiler.gpu-sync", false);

    /// <summary>
    /// Frames measured per configuration, per round, during a sweep.
    /// </summary>
    public static readonly CVarDef<int> SweepFramesPerRound =
        CVarDef.Create("profiler.sweep-frames-per-round", 20);

    /// <summary>
    /// Rounds a sweep cycles through every configuration. Multiple rounds,
    /// interleaved instead of one contiguous block per configuration, is what
    /// keeps a thermal/clock drift trend from reading as a difference between
    /// configurations - see Docs/Content/Profiling.md.
    /// </summary>
    public static readonly CVarDef<int> SweepRounds =
        CVarDef.Create("profiler.sweep-rounds", 5);
}
