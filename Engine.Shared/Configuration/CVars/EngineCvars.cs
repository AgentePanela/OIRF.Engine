namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public sealed class EngineCvars
{
    public static readonly CVarDef<string> EngineVersion =
        CVarDef.Create("engine.version", "IN-DEV 1.0.0");

    public static readonly CVarDef<int> SystemProfillerTop =
        CVarDef.Create("engine.system-profiller-top", 10);

    public static readonly CVarDef<bool> ProfilerEnabled =
        CVarDef.Create("engine.profiler.enabled", true);

    /// <summary>
    /// How many frames the profiler keeps for its rolling averages.
    /// </summary>
    public static readonly CVarDef<int> ProfilerWindow =
        CVarDef.Create("engine.profiler.window", 120);

    /// <summary>
    /// Drains the GPU pipeline after each render pass so its timer measures
    /// real GPU cost instead of command submission. Makes the frame much
    /// slower on purpose - the numbers are only comparable to each other.
    /// </summary>
    public static readonly CVarDef<bool> ProfilerGpuSync =
        CVarDef.Create("engine.profiler.gpu-sync", false);

    /// <summary>
    /// Frames measured per configuration during a profiler sweep.
    /// </summary>
    public static readonly CVarDef<int> ProfilerSweepFrames =
        CVarDef.Create("engine.profiler.sweep-frames", 60);
}
