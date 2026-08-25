using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Lighting;
using Engine.Client.UI;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.Debug.Diagnostics;
using Engine.Shared.IoC;
using Engine.Shared.Storage;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;

namespace Engine.Client.Debug.Diagnostics;

/// <summary>
/// Builds the full frame report - where the frame goes, what the renderer did
/// and what it cost - and writes it next to the rest of the user data. See
/// Docs/Content/Profiling.md for how to read it.
/// </summary>
public static class ProfilerReport
{
    private const int NameColumn = 34;

    /// <summary>
    /// Renders the report and writes it to logs/profile-{timestamp}.log.
    /// Returns the full path, or null if writing failed.
    /// </summary>
    public static string? Dump(string? tag = null)
    {
        try
        {
            var storage = IoCManager.Resolve<UserStorageManager>();
            var suffix = string.IsNullOrEmpty(tag) ? "" : $"-{tag}";
            var relative = $"logs/profile-{DateTime.Now:yyyy.MM.dd-HH.mm.ss}{suffix}.log";

            var text = Build();
            storage.WriteText(relative, text);

            // "since last dump" in the MEMORY section should mean since this
            // dump, not since the process before it - reset after reading it
            GameClient.GCTracker.MarkCheckpoint();

            return storage.GetFullPath(relative);
        }
        catch (Exception e)
        {
            Log.Error($"Could not write the profiler report: {e.Message}");
            return null;
        }
    }

    public static string Build()
    {
        var sb = new StringBuilder(16 * 1024);

        var profiler = GameClient.Profiler;
        var stats = GameClient.RenderStats;
        var lighting = IoCManager.Resolve<LightingManager>();
        var systems = IoCManager.Resolve<SystemsProfiler>();
        var cfg = GameClient.ConfigManager;

        WriteHeader(sb, cfg, lighting, profiler);
        WriteUser(sb);
        WriteVerdict(sb, profiler);
        WriteFrameBudget(sb, profiler);
        WritePipeline(sb, profiler);
        WriteBatchBreaks(sb, stats);
        WriteTargets(sb, stats);
        WriteFill(sb, stats);
        WriteLighting(sb, lighting);
        WriteSweep(sb);
        WriteSystems(sb, systems);
        WriteUi(sb);
        WriteMemory(sb);
        WriteCensus(sb);

        return sb.ToString();
    }

    #region Sections

    // Scopes that had nothing nested under them in the last real frame - the
    // set that partitions the frame without a parent's time double counting
    // its children's.
    private static IEnumerable<(string Name, double AvgMs)> LeafScopes(FrameProfiler profiler)
    {
        var avgByName = new Dictionary<string, double>();
        foreach (var scope in profiler.GetStats())
            avgByName[scope.Name] = scope.AvgMs;

        // a name can open more than once in the same "frame" (Update runs
        // several times in a row when catching up on a fixed timestep) -
        // only its first occurrence should count towards the ranking
        var seen = new HashSet<string>();
        var samples = profiler.LastFrameSamples;
        for (int i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var isLeaf = i + 1 >= samples.Count || samples[i + 1].Depth <= sample.Depth;
            if (isLeaf && seen.Add(sample.Name) && avgByName.TryGetValue(sample.Name, out var avgMs))
                yield return (sample.Name, avgMs);
        }
    }

    private static void WriteHeader(StringBuilder sb, IConfigurationManager cfg, LightingManager lighting,
        FrameProfiler profiler)
    {
        Title(sb, "BUILD");

        var gfx = GameClient.Graphics;
        var viewport = GameClient.Viewport;

        Row(sb, "generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Row(sb, "game", $"{GameClient.Options.Title} {cfg.Get(GameCVars.GameVersion)}");
        Row(sb, "engine", cfg.Get(EngineCvars.EngineVersion));
        Row(sb, "platform", $"{PlatformInfo.GraphicsBackend} | {PlatformInfo.MonoGamePlatform}");
        Row(sb, "backbuffer", $"{gfx.PreferredBackBufferWidth}x{gfx.PreferredBackBufferHeight}");
        Row(sb, "virtual viewport", $"{viewport.VirtualWidth}x{viewport.VirtualHeight}");
        Row(sb, "camera zoom", $"{GameClient.Camera.Zoom:0.00}x");
        Row(sb, "sampler", GameClient.Options.Samplimg == SamplerState.PointClamp ? "PointClamp" : "other");
        Row(sb, "lighting", lighting.Enabled
            ? $"on (lightmap scale {lighting.LightmapScale:0.00}, shadow map cap {lighting.ShadowMapSize}x{lighting.MaxShadowcastingLights})"
            : "off");
        Row(sb, "uptime", FormatTime(GameClient.GameTime.TotalTime));
        Row(sb, "profiler window", $"{profiler.SampledFrames}/{profiler.WindowSize} frames");

        var vsync = gfx.SynchronizeWithVerticalRetrace;
        var fixedStep = GameClient.Instance.IsFixedTimeStep;
        Row(sb, "vsync", vsync ? "on" : "off");
        Row(sb, "fixed timestep", fixedStep ? "on" : "off");
        Row(sb, "gpu-sync", profiler.GpuSyncEnabled ? "on | frame time is inflated" : "off");
        Row(sb, "measurement mode", DescribeMeasurementMode(vsync, profiler.GpuSyncEnabled));

        sb.AppendLine();
    }

    // Operationalizes the "three runs" methodology from Docs/Content/Profiling.md
    // directly in the header, so which of the three this report is can never be
    // misread from the numbers alone.
    private static string DescribeMeasurementMode(bool vsync, bool gpuSync) => (vsync, gpuSync) switch
    {
        (false, false) => "CPU only, uncapped - pure CPU cost; GPU work is still queued async and not counted here",
        (false, true) => "CPU+GPU serialized, uncapped - closest to real GPU cost, but every gpu-sync scope stalls the pipeline so absolute numbers are inflated",
        (true, false) => "real frame, vsync capped - what the player sees; present/vsync wait below is idle time, not a cost",
        (true, true) => "real frame, GPU forced serial - numbers are inflated on purpose here, only compare passes against each other",
    };

    private static void WriteUser(StringBuilder sb)
    {
        Title(sb, "USER");

        var adapter = GraphicsAdapter.DefaultAdapter;
        var device = GameClient.GraphicsDevice;
        var memory = GC.GetGCMemoryInfo();

        Row(sb, "os", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Row(sb, "runtime", RuntimeInformation.FrameworkDescription);
        Row(sb, "process", $"{RuntimeInformation.ProcessArchitecture}, " +
                           $"{(Environment.Is64BitProcess ? "64 bit" : "32 bit")}, " +
                           $"{(GCSettings.IsServerGC ? "server GC" : "workstation GC")}");
        Row(sb, "cpu", GetCpuName());
        Row(sb, "cpu cores", Environment.ProcessorCount.ToString());

        if (memory.TotalAvailableMemoryBytes > 0)
            Row(sb, "system memory", FormatBytes(memory.TotalAvailableMemoryBytes));

        Row(sb, "culture", CultureInfo.CurrentCulture.Name);
        sb.AppendLine();

        Row(sb, "gpu", DescribeAdapter(adapter));

        var profile = device?.GraphicsProfile;
        Row(sb, "graphics profile", profile is null
            ? "unknown"
            : $"{profile} (max texture {MaxTextureSize(profile.Value)})");

        var mode = adapter.CurrentDisplayMode;
        if (mode is not null)
            Row(sb, "desktop mode", $"{mode.Width}x{mode.Height} {mode.Format} ({mode.AspectRatio:0.00}:1)");

        Row(sb, "window", GameClient.Graphics.IsFullScreen ? "fullscreen" : "windowed");

        var adapters = GraphicsAdapter.Adapters;
        if (adapters.Count > 1)
        {
            Row(sb, "adapters", adapters.Count.ToString());
            foreach (var other in adapters)
                sb.AppendLine($"    {(other == adapter ? "*" : " ")} {DescribeAdapter(other)}");
        }

        sb.AppendLine();
    }

    // DesktopGL often leaves the adapter description empty
    private static string DescribeAdapter(GraphicsAdapter adapter)
        => string.IsNullOrWhiteSpace(adapter.Description) ? "unknown" : adapter.Description;

    private static string MaxTextureSize(GraphicsProfile profile)
        => profile == GraphicsProfile.Reach ? "2048px" : "4096px";

    private static void WriteVerdict(StringBuilder sb, FrameProfiler profiler)
    {
        Title(sb, "VERDICT");

        if (profiler.SampledFrames == 0)
        {
            sb.AppendLine("  no frames sampled yet - is profiler.enabled off?");
            sb.AppendLine();
            return;
        }

        var cpuMs = profiler.FrameAvgMs;
        var presentMs = profiler.PresentAvgMs;
        var totalMs = cpuMs + presentMs;

        var vsync = GameClient.Graphics.SynchronizeWithVerticalRetrace;
        var gpuSync = profiler.GpuSyncEnabled;
        var vsyncPeriodMs = GameClient.Instance.TargetElapsedTime.TotalMilliseconds;

        // vsync on and the frame roughly matching the expected period is a
        // CPU that finished early and is idling until the next refresh - the
        // healthy, expected shape of a vsync'd frame, not a GPU bottleneck.
        // Present/vsync wait only means "GPU bound" when gpu-sync is actually
        // making it carry GPU cost, or when the frame overran its budget
        // despite vsync being on (a real, missed-frame problem).
        var withinVsyncBudget = vsync && totalMs <= vsyncPeriodMs * 1.1;

        if (vsync && !gpuSync && withinVsyncBudget)
        {
            var cpuBudgetPct = vsyncPeriodMs <= 0 ? 0 : cpuMs / vsyncPeriodMs * 100.0;
            sb.AppendLine($"  VSYNC-LIMITED - CPU finishes in {FormatMs(cpuMs)} of a {FormatMs(vsyncPeriodMs)} frame budget ({cpuBudgetPct:0}%) and idles the rest ({FormatMs(presentMs)}).");
            sb.AppendLine("  This is healthy vsync behaviour, not a GPU bottleneck. To see real GPU cost,");
            sb.AppendLine("  turn profiler.gpu-sync on (inflates frame time on purpose) or run with vsync off.");
        }
        else if (vsync && !gpuSync)
        {
            sb.AppendLine($"  MISSED FRAMES - total frame time {FormatMs(totalMs)} exceeds the {FormatMs(vsyncPeriodMs)} vsync budget even with vsync on.");
            sb.AppendLine("  Something is overrunning the frame - cpu frame avg below is where to start.");
        }
        else if (gpuSync)
        {
            var presentShare = totalMs <= 0 ? 0 : presentMs / totalMs * 100.0;
            sb.AppendLine($"  gpu-sync is on: present/vsync wait ({FormatMs(presentMs)}, {presentShare:0.0}% of the frame) now includes real GPU cost, not idle wait.");
            sb.AppendLine(presentShare > 55
                ? "  GPU-BOUND - most of the frame is GPU work, not CPU submission."
                : "  CPU-BOUND - most of the frame is still CPU work even with the GPU forced to keep up.");
        }
        else
        {
            // vsync off, gpu-sync off: presentMs here is swap/driver overhead,
            // not vsync idle - the closest thing to a pure, uncapped CPU number
            sb.AppendLine($"  CPU cost (uncapped): {FormatMs(cpuMs)}. GPU cost isn't visible without gpu-sync or vsync on - see the sweep below.");
        }

        sb.AppendLine();
        sb.AppendLine("  Top offenders (leaf scopes only, so times don't overlap a parent's):");

        var frameTotal = Math.Max(0.0001, cpuMs);
        foreach (var (name, ms) in LeafScopes(profiler).OrderByDescending(o => o.AvgMs).Take(5))
            sb.AppendLine($"    {name.PadRight(NameColumn)} {FormatMs(ms),10}  {ms / frameTotal * 100,5:0.0}% of cpu frame");

        // self-check: GameClient.GameTime.Fps is measured independently (Draw
        // calls per wall-clock second). If it disagrees with what cpu+present
        // implies, one of the two numbers is wrong - worth a loud warning
        // instead of two figures that quietly don't add up.
        var reportedFps = GameClient.GameTime.Fps;
        var derivedFps = totalMs <= 0 ? 0 : 1000.0 / totalMs;
        if (reportedFps > 0 && derivedFps > 0)
        {
            var diff = Math.Abs(reportedFps - derivedFps) / reportedFps;
            if (diff > 0.10)
            {
                sb.AppendLine();
                sb.AppendLine($"  ⚠ fps mismatch: GameTime.Fps reports {reportedFps}, but cpu+present implies {derivedFps:0.0} - " +
                               "one of the two is wrong, don't trust either number until this is resolved.");
            }
        }

        sb.AppendLine();
    }

    private static void WriteFrameBudget(StringBuilder sb, FrameProfiler profiler)
    {
        Title(sb, "FRAME");

        var cpu = profiler.FrameAvgMs;
        var present = profiler.PresentAvgMs;
        var vsync = GameClient.Graphics.SynchronizeWithVerticalRetrace;
        var vsyncPeriodMs = GameClient.Instance.TargetElapsedTime.TotalMilliseconds;

        Row(sb, "fps (measured)", GameClient.GameTime.Fps.ToString());
        Row(sb, "cpu frame avg", FormatMs(cpu));
        Row(sb, "cpu frame min / max", $"{FormatMs(profiler.FrameMinMs)} / {FormatMs(profiler.FrameMaxMs)}");
        Row(sb, "cpu frame p95", FormatMs(profiler.FramePercentileMs(0.95)));
        Row(sb, "present / vsync wait", $"{FormatMs(present)} (max {FormatMs(profiler.PresentMaxMs)})");
        Row(sb, "total measured", FormatMs(cpu + present));

        // Idle vsync wait is not budget spent - only the CPU share is "used".
        // Counting present/vsync wait as used here is the bug that used to
        // print "101.6% used" on a perfectly healthy vsync'd frame.
        Row(sb, "vsync budget", vsync
            ? $"{FormatMs(vsyncPeriodMs)} - {(vsyncPeriodMs <= 0 ? 0 : cpu / vsyncPeriodMs * 100.0):0.0}% used by CPU (present/vsync wait is idle, not budget)"
            : "n/a (vsync off)");

        sb.AppendLine();
    }

    private static void WritePipeline(StringBuilder sb, FrameProfiler profiler)
    {
        Title(sb, "FRAME PHASES");
        sb.AppendLine();

        var frame = Math.Max(0.0001, profiler.FrameAvgMs);
        sb.AppendLine($"  {"phase".PadRight(NameColumn)} {"avg",10} {"max",10} {"%frame",8} {"alloc/f",12} {"calls",7}");

        var stats = new Dictionary<string, ScopeStats>();
        foreach (var scope in profiler.GetStats())
            stats[scope.Name] = scope;

        void Line(ScopeStats scope, int depth, string suffix = "")
        {
            var name = new string(' ', depth * 2) + scope.Name;
            sb.AppendLine(
                $"  {Truncate(name, NameColumn).PadRight(NameColumn)} " +
                $"{FormatMs(scope.AvgMs),10} {FormatMs(scope.MaxMs),10} " +
                $"{scope.AvgMs / frame * 100,7:0.0}% {FormatBytes(scope.AvgAllocBytes),12} {scope.AvgCalls,7:0.0}{suffix}");
        }

        // walk the last frame, so the order and nesting are the ones that
        // actually happened rather than the order scopes were first seen in
        var seen = new HashSet<string>();
        foreach (var sample in profiler.LastFrameSamples)
        {
            if (!seen.Add(sample.Name))
                continue;
            if (stats.TryGetValue(sample.Name, out var scope))
                Line(scope, sample.Depth);
        }

        // conditional passes that skipped the last frame still have history
        foreach (var scope in stats.Values)
        {
            if (!seen.Contains(scope.Name))
                Line(scope, scope.Depth, "   (idle last frame)");
        }

        sb.AppendLine();
    }

    private static void WriteBatchBreaks(StringBuilder sb, RenderStats stats)
    {
        Title(sb, "DRAW CALLS / BATCH BREAKS");

        var draws = stats.Get(RenderCounter.MetricsDrawDelta).Avg;
        var primitives = stats.Get(RenderCounter.MetricsPrimitiveDelta).Avg;
        var sprites = stats.Get(RenderCounter.MetricsSpriteDelta).Avg;

        sb.AppendLine("  Draw call/primitive counts come straight from GraphicsDevice.Metrics around");
        sb.AppendLine("  the world render queue - no hand-counting to fall out of date.");
        sb.AppendLine();
        Row(sb, "draw calls (world queue)", $"{draws:0.0} avg");
        Row(sb, "primitives (world queue)", $"{primitives:0.0} avg");
        Row(sb, "sprites (world queue)", $"{sprites:0.0} avg");
        Row(sb, "sprite batches", $"{stats.Get(RenderCounter.SpriteBatches).Avg:0.0} avg");
        Row(sb, "shape batches", $"{stats.Get(RenderCounter.ShapeBatches).Avg:0.0} avg");

        if (stats.AvgBatches > 0 && draws > 0)
        {
            var perBatch = draws / stats.AvgBatches;
            sb.AppendLine();
            Row(sb, "draw calls per batch", $"{perBatch:0.0}   (higher is better)");
            if (perBatch < 8 && draws > 50)
                sb.AppendLine("  > POOR BATCHING - something is breaking the batch far more than the geometry needs.");
        }

        sb.AppendLine();
        void Line(string label, RenderCounter counter)
        {
            var s = stats.Get(counter);
            if (s.Avg <= 0 && s.Max <= 0) return;
            sb.AppendLine($"  {label.PadRight(NameColumn)} {s.Avg,10:0.0} avg  {s.Max,10:0} max");
        }

        Line("shader switch", RenderCounter.BreakShader);
        Line("sampler switch", RenderCounter.BreakSampler);
        Line("shaded/unshaded switch", RenderCounter.BreakUnshaded);
        Line("sprite -> shape", RenderCounter.BreakSpriteToShape);
        Line("shape -> sprite", RenderCounter.BreakShapeToSprite);
        sb.AppendLine($"  {"TOTAL breaks".PadRight(NameColumn)} {stats.AvgBatchBreaks,10:0.0} avg");

        WriteTransitions(sb, "top shader transitions (lifetime count)", stats.ShaderTransitions);
        WriteTransitions(sb, "top sampler transitions (lifetime count)", stats.SamplerTransitions);

        sb.AppendLine();
        Row(sb, "queue sort", $"{FormatMs(stats.SortStats.Avg)} (max {FormatMs(stats.SortStats.Max)})");
        Row(sb, "queue peak", stats.QueuePeak.ToString());
        sb.AppendLine();
    }

    private static void WriteTransitions(StringBuilder sb, string title,
        IEnumerable<KeyValuePair<(string From, string To), int>> transitions)
    {
        var top = transitions.OrderByDescending(t => t.Value).Take(5).ToList();
        if (top.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"  {title}:");
        foreach (var t in top)
            sb.AppendLine($"    {t.Key.From} -> {t.Key.To}".PadRight(NameColumn + 6) + $" {t.Value,8} times");
    }

    private static void WriteTargets(StringBuilder sb, RenderStats stats)
    {
        Title(sb, "RENDER TARGETS");
        sb.AppendLine($"  {"target".PadRight(20)} {"size",13} {"format",10} {"vram",10} {"binds/f",8} {"clears/f",9} {"realloc",8}");

        long totalBytes = 0;
        double trackedBinds = 0, trackedClears = 0;
        foreach (var target in stats.Targets)
        {
            totalBytes += target.Bytes;
            trackedBinds += target.BindsHistory.Average();
            trackedClears += target.ClearsHistory.Average();
            sb.AppendLine(
                $"  {Truncate(target.Name, 20).PadRight(20)} {$"{target.Width}x{target.Height}",13} " +
                $"{target.Format,10} {FormatBytes(target.Bytes),10} {target.BindsHistory.Average(),8:0.0} " +
                $"{target.ClearsHistory.Average(),9:0.0} {target.Reallocs,8}");
        }

        sb.AppendLine();
        sb.AppendLine("  binds/clears are the window average; realloc is a lifetime count, detected");
        sb.AppendLine("  automatically whenever a name is bound to a different target reference than");
        sb.AppendLine("  last time - a target resizing every frame would show up here on its own.");
        sb.AppendLine();
        Row(sb, "render target VRAM", FormatBytes(totalBytes));

        var reallocs = stats.Targets.Sum(t => t.Reallocs);
        if (reallocs > 4)
            sb.AppendLine($"  > {reallocs} render target reallocation(s) (lifetime) - a target may be resizing every frame.");

        // GraphicsDevice.Metrics is an independent ground truth for how many
        // SetRenderTarget/Clear calls actually happened - if it disagrees
        // with what got tracked above, some pass is bypassing
        // GraphicsDeviceProfilerExtensions and needs to be found and fixed,
        // instead of quietly reading as binds/f 0.0 forever.
        var metricTargets = stats.Get(RenderCounter.MetricsTargetDelta).Avg;
        var metricClears = stats.Get(RenderCounter.MetricsClearDelta).Avg;
        sb.AppendLine();
        Row(sb, "target binds/f (tracked / GraphicsDevice.Metrics)", $"{trackedBinds:0.0} / {metricTargets:0.0}");
        Row(sb, "clears/f (tracked / GraphicsDevice.Metrics)", $"{trackedClears:0.0} / {metricClears:0.0}");

        if (metricTargets > trackedBinds + 0.5)
            sb.AppendLine($"  ⚠ {metricTargets - trackedBinds:0.0} render target switch(es)/frame aren't going through SetRenderTargetTracked.");
        if (metricClears > trackedClears + 0.5)
            sb.AppendLine($"  ⚠ {metricClears - trackedClears:0.0} Clear() call(s)/frame aren't going through ClearTracked.");

        sb.AppendLine();
    }

    private static void WriteFill(StringBuilder sb, RenderStats stats)
    {
        Title(sb, "FILL RATE (EST, LIGHTING PASSES)");
        sb.AppendLine();

        var total = stats.TotalAvgFill;
        if (total <= 0)
        {
            sb.AppendLine("  nothing recorded (lighting off, or no lighting passes ran this window).");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"  {"pass".PadRight(NameColumn)} {"MPixels/f",12} {"%",8} {"max",12}");
        foreach (var (pass, avg, max) in stats.Fill.OrderByDescending(f => f.AvgPixels))
        {
            sb.AppendLine(
                $"  {Truncate(pass, NameColumn).PadRight(NameColumn)} {avg / 1e6,12:0.00} " +
                $"{avg / total * 100,7:0.0}% {max / 1e6,12:0.00}");
        }

        sb.AppendLine();
        Row(sb, "total", $"{total / 1e6:0.00} MPixels/frame");

        var fps = Math.Max(1, GameClient.GameTime.Fps);
        Row(sb, "at current fps", $"{total * fps / 1e6:0.0} MPixels/s");

        var screen = (double)GameClient.Graphics.PreferredBackBufferWidth * GameClient.Graphics.PreferredBackBufferHeight;
        if (screen > 0)
            Row(sb, "overdraw", $"{total / screen:0.00}x the screen");

        sb.AppendLine();
    }

    private static void WriteLighting(StringBuilder sb, LightingManager lighting)
    {
        Title(sb, "LIGHTING");

        if (!lighting.Enabled)
        {
            sb.AppendLine("  disabled.");
            sb.AppendLine();
            return;
        }

        Row(sb, "total", FormatMs(lighting.LastLightingTotalMs));
        Row(sb, "  shadow pass", FormatMs(lighting.LastShadowPassMs));
        Row(sb, "    geometry build", FormatMs(lighting.LastShadowBuildMs));
        Row(sb, "    bind + clear", FormatMs(lighting.LastShadowSetupMs));
        Row(sb, "    draws", FormatMs(lighting.LastShadowDrawMs));
        Row(sb, "  light pass", FormatMs(lighting.LastLightPassMs));
        Row(sb, "  wall bleed", FormatMs(lighting.LastWallBleedMs));
        Row(sb, "  light blur", FormatMs(lighting.LastLightBlurMs));
        sb.AppendLine();
        Row(sb, "visible lights", lighting.LastVisibleLights.ToString());
        Row(sb, "shadow casting lights", lighting.LastShadowLights.ToString());
        Row(sb, "occluders", lighting.LastOccluders.ToString());
        Row(sb, "shadow map (active rows)", $"{lighting.LastShadowMapWidth}x{lighting.LastShadowMapHeight}");
        sb.AppendLine();
        sb.AppendLine("  shadow map rows grow with active shadow-casting lights (rounded up to a");
        sb.AppendLine("  power of 2), capped at the BUILD section's 'shadow map cap' - a small active");
        sb.AppendLine("  count here is normal, not an error.");
        sb.AppendLine();
        sb.AppendLine("  occluder edge geometry (geometry build above) is rebuilt every frame - a");
        sb.AppendLine("  known, not-yet-fixed cost, not a profiler bug (LightingSystem.CollectOccluders).");
        sb.AppendLine();
        sb.AppendLine("  these are this frame's numbers, not a window average - 'total' also includes");
        sb.AppendLine("  light/occluder collection, so a small gap against the sum of the rows above,");
        sb.AppendLine("  and against ECS SYSTEMS' windowed 'LightingSystem' average, is expected.");
        sb.AppendLine();
    }

    private static void WriteSweep(StringBuilder sb)
    {
        var sweep = GameClient.Sweep;
        if (sweep.Results.Count == 0)
            return;

        Title(sb, "PASS ISOLATION SWEEP");
        sb.AppendLine();
        sb.AppendLine("  configurations in declared order (baseline first), not sorted by cost - each");
        sb.AppendLine("  measured across several short interleaved rounds so a thermal/clock drift");
        sb.AppendLine("  over the sweep's runtime lands on every configuration equally instead of");
        sb.AppendLine("  piling onto whichever one happened to run last.");
        sb.AppendLine();
        sb.AppendLine($"  {"configuration".PadRight(NameColumn)} {"median",10} {"saved",10} {"spread",10} {"fps",8}");

        var baselineMs = sweep.Results[0].MedianMs;
        foreach (var result in sweep.Results)
        {
            var fps = result.MedianMs <= 0 ? 0 : 1000.0 / result.MedianMs;
            var noiseFloor = Math.Max(0.05, baselineMs * 0.05);
            var warning = "";
            if (result.SpreadMs > Math.Abs(result.SavedMs) && result.SpreadMs > 0)
                warning = "  ⚠ spread exceeds the difference - likely noise";
            else if (result.SavedMs < -noiseFloor)
                warning = "  ⚠ slower than baseline - contamination suspected, don't trust this number";

            sb.AppendLine(
                $"  {Truncate(result.Name, NameColumn).PadRight(NameColumn)} {FormatMs(result.MedianMs),10} " +
                $"{FormatMs(result.SavedMs),10} {FormatMs(result.SpreadMs),10} {fps,8:0}{warning}");
        }
        sb.AppendLine();
    }

    private static void WriteSystems(StringBuilder sb, SystemsProfiler systems)
    {
        Title(sb, "ECS SYSTEMS");
        sb.AppendLine($"  {"system".PadRight(NameColumn)} {"update",10} {"draw",10} {"total",10}");

        var all = systems.GetAll().OrderByDescending(s => s.TotalMs).ToList();
        double totalUpdate = 0, totalDraw = 0;

        foreach (var system in all)
        {
            totalUpdate += system.UpdateMs;
            totalDraw += system.DrawMs;

            sb.AppendLine(
                $"  {Truncate(system.Name, NameColumn).PadRight(NameColumn)} " +
                $"{FormatMs(system.UpdateMs),10} {FormatMs(system.DrawMs),10} {FormatMs(system.TotalMs),10}");
        }

        sb.AppendLine();
        Row(sb, "systems tracked", all.Count.ToString());
        Row(sb, "sum of update", FormatMs(totalUpdate));
        Row(sb, "sum of draw", FormatMs(totalDraw));
        sb.AppendLine();
    }

    private static void WriteUi(StringBuilder sb)
    {
        Title(sb, "UI");
        sb.Append(UIProfiler.LogSnapshot());
        sb.AppendLine();
    }

    private static void WriteMemory(StringBuilder sb)
    {
        Title(sb, "MEMORY / GC");

        var tracker = GameClient.GCTracker;
        var sinceStart = tracker.SinceStart;
        var sinceCheckpoint = tracker.SinceCheckpoint;

        Row(sb, "GC since boot",
            $"gen0 {sinceStart.Gen0} | gen1 {sinceStart.Gen1} | gen2 {sinceStart.Gen2} | allocated {FormatBytes(sinceStart.AllocatedBytes)}");
        Row(sb, "GC since last dump",
            $"gen0 {sinceCheckpoint.Gen0} | gen1 {sinceCheckpoint.Gen1} | gen2 {sinceCheckpoint.Gen2} | allocated {FormatBytes(sinceCheckpoint.AllocatedBytes)}");
        sb.AppendLine();

        var process = Process.GetCurrentProcess();
        Row(sb, "managed memory", FormatBytes(GC.GetTotalMemory(false)));
        Row(sb, "process memory", FormatBytes(process.WorkingSet64));

        var atlasPages = GameClient.Assets.GetAllAtlasses();
        long textureMemory = 0;
        foreach (var atlas in atlasPages)
            textureMemory += (long)atlas.Texture.Width * atlas.Texture.Height * 4;
        Row(sb, "estimated VRAM (textures)", FormatBytes(textureMemory));
        Row(sb, "atlas pages", atlasPages.Count.ToString());

        sb.AppendLine();
    }

    private static void WriteCensus(StringBuilder sb)
    {
        Title(sb, "CONTENT");

        Row(sb, "entities", GameClient.EntityManager.GetEntityCount().ToString());
        Row(sb, "prototypes", GameClient.Prototypes.Count().ToString());
        Row(sb, "atlas pages", GameClient.Assets.GetAllAtlasses().Count.ToString());
        sb.AppendLine();
    }

    #endregion

    #region Formatting

    private static void Title(StringBuilder sb, string title)
    {
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($" {title}");
        sb.AppendLine(new string('=', 78));
    }

    private static void Row(StringBuilder sb, string label, string value)
        => sb.AppendLine($"  {label.PadRight(NameColumn)} {value}");

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    // handles negative values (e.g. a sweep "saved" a config is slower, or a
    // GC delta) - falling through every >= check silently prints "0" for a
    // real, meaningful negative number, which is how the old report hid a
    // sweep config that regressed instead of flagging it.
    private static string FormatMs(double ms)
    {
        var sign = ms < 0 ? "-" : "";
        var abs = Math.Abs(ms);
        if (abs >= 1000.0) return $"{sign}{abs / 1000.0:0.00}s";
        if (abs >= 1.0) return $"{sign}{abs:0.00}ms";
        if (abs >= 0.001) return $"{sign}{abs * 1000.0:0.0}us";
        return "0";
    }

    private static string FormatBytes(double bytes)
    {
        var sign = bytes < 0 ? "-" : "";
        var abs = Math.Abs(bytes);
        if (abs >= 1024 * 1024) return $"{sign}{abs / 1024 / 1024:0.00}MB";
        if (abs >= 1024) return $"{sign}{abs / 1024:0.0}KB";
        return $"{sign}{abs:0}B";
    }

    private static string FormatTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? span.ToString(@"hh\:mm\:ss") : span.ToString(@"mm\:ss");
    }

    private static string GetCpuName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            const string path = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue("ProcessorNameString")?.ToString() ?? "Unknown Windows CPU";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/cpuinfo"))
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                    return line.Split(':')[1].Trim();
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return ExecuteCommand("sysctl", "-n machdep.cpu.brand_string");

        return "Unknown OS / CPU";
    }

    private static string ExecuteCommand(string filename, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = filename,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        using var reader = process?.StandardOutput;
        return reader?.ReadToEnd().Trim() ?? "Unknown";
    }

    #endregion
}
