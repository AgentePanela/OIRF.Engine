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
using MonoGame.Framework.Utilities;

namespace Engine.Client.Debug.Diagnostics;

/// <summary>
/// Builds the full frame report - where the frame goes, what the renderer did
/// and what it cost - and writes it next to the rest of the user data.
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

            storage.WriteText(relative, Build());
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
        WriteVerdict(sb, profiler, stats);
        WriteFrameBudget(sb, profiler);
        WritePipeline(sb, profiler);
        WriteDrawCalls(sb, stats);
        WriteTargets(sb, stats);
        WriteFill(sb, stats, profiler);
        WriteDrawSystems(sb, stats);
        WriteLighting(sb, lighting);
        WriteSweep(sb);
        WriteSystems(sb, systems, profiler);
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
        Row(sb, "sampler", GameClient.Options.Samplimg == Microsoft.Xna.Framework.Graphics.SamplerState.PointClamp
            ? "PointClamp" : "other");
        Row(sb, "lighting", lighting.Enabled
            ? $"on (lightmap scale {lighting.LightmapScale:0.00}, shadow map cap {lighting.ShadowMapSize}x{lighting.MaxShadowcastingLights})"
            : "off");
        Row(sb, "uptime", FormatTime(GameClient.GameTime.TotalTime));
        Row(sb, "profiler window", $"{profiler.SampledFrames}/{profiler.WindowSize} frames");
        Row(sb, "gpu-sync", profiler.GpuSyncEnabled ? "on | frame time is inflated" : "off");

        var vsync = gfx.SynchronizeWithVerticalRetrace;
        var fixedStep = GameClient.Instance.IsFixedTimeStep;
        Row(sb, "vsync", vsync ? "on" : "off");
        Row(sb, "fixed timestep", fixedStep ? "on" : "off");

        sb.AppendLine();
    }

    private static void WriteUser(StringBuilder sb)
    {
        Title(sb, "USER");

        var adapter = Microsoft.Xna.Framework.Graphics.GraphicsAdapter.DefaultAdapter;
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

        var adapters = Microsoft.Xna.Framework.Graphics.GraphicsAdapter.Adapters;
        if (adapters.Count > 1)
        {
            Row(sb, "adapters", adapters.Count.ToString());
            foreach (var other in adapters)
                sb.AppendLine($"    {(other == adapter ? "*" : " ")} {DescribeAdapter(other)}");
        }

        sb.AppendLine();
    }

    // DesktopGL often leaves the adapter description empty
    private static string DescribeAdapter(Microsoft.Xna.Framework.Graphics.GraphicsAdapter adapter)
        => string.IsNullOrWhiteSpace(adapter.Description) ? "unknown" : adapter.Description;

    private static string MaxTextureSize(Microsoft.Xna.Framework.Graphics.GraphicsProfile profile)
        => profile == Microsoft.Xna.Framework.Graphics.GraphicsProfile.Reach ? "2048px" : "4096px";

    private static void WriteVerdict(StringBuilder sb, FrameProfiler profiler, RenderStats stats)
    {
        Title(sb, "RESUME");

        var cpuMs = profiler.FrameAvgMs;
        var presentMs = profiler.PresentAvgMs;
        var total = cpuMs + presentMs;

        if (profiler.SampledFrames == 0)
        {
            sb.AppendLine("  no frames sampled yet - is engine.profiler.enabled off?");
            sb.AppendLine();
            return;
        }

        // if most of the frame is spent waiting to present, the cpu side is
        // already done and making it faster buys nothing
        var presentShare = total <= 0 ? 0 : presentMs / total * 100.0;
        if (presentShare > 55)
        {
            sb.AppendLine($"  GPU / VSYNC - {presentShare:0}% of the frame is spent waiting to present.");
        }
        else
        {
            sb.AppendLine($"  CPU BOUND - {100 - presentShare:0}% of the frame is CPU work ({FormatMs(cpuMs)} vs {FormatMs(presentMs)} presenting).");
        }

        sb.AppendLine();
        sb.AppendLine("  Top offenders (leaf scopes only, so times don't overlap a parent's):");

        var frameTotal = Math.Max(0.0001, cpuMs);
        foreach (var (name, ms) in LeafScopes(profiler).OrderByDescending(o => o.AvgMs).Take(5))
            sb.AppendLine($"    {name.PadRight(NameColumn)} {FormatMs(ms),10}  {ms / frameTotal * 100,5:0.0}% of cpu frame");

        if (stats.AvgBatches > 0)
        {
            var perBatch = stats.AvgDrawCalls / stats.AvgBatches;
            if (perBatch < 8 && stats.AvgDrawCalls > 50)
                sb.AppendLine($"    POOR BATCHING: {perBatch:0.0} draw calls per batch");
        }

        sb.AppendLine();
    }

    private static void WriteFrameBudget(StringBuilder sb, FrameProfiler profiler)
    {
        Title(sb, "FRAME");

        var cpu = profiler.FrameAvgMs;
        var present = profiler.PresentAvgMs;

        Row(sb, "fps", GameClient.GameTime.Fps.ToString());
        Row(sb, "cpu frame avg", FormatMs(cpu));
        Row(sb, "cpu frame min / max", $"{FormatMs(profiler.FrameMinMs)} / {FormatMs(profiler.FrameMaxMs)}");
        Row(sb, "cpu frame p95", FormatMs(profiler.FramePercentileMs(0.95)));
        Row(sb, "present / vsync wait", $"{FormatMs(present)} (max {FormatMs(profiler.PresentMaxMs)})");
        Row(sb, "total per frame", FormatMs(cpu + present));
        Row(sb, "60 fps", $"16.67ms - {(cpu + present) / 16.67 * 100:0.0}% used");
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

    private static void WriteDrawCalls(StringBuilder sb, RenderStats stats)
    {
        Title(sb, "DRAW CALLS");

        void Line(string label, RenderCounter counter)
        {
            var s = stats.Get(counter);
            if (s.Avg <= 0 && s.Max <= 0)
                return;
            sb.AppendLine($"  {label.PadRight(NameColumn)} {s.Avg,10:0.0} avg  {s.Max,10:0} max");
        }

        Line("sprites", RenderCounter.Sprites);
        Line("raw sprites (atlas)", RenderCounter.RawSprites);
        Line("raw textures", RenderCounter.RawTextures);
        Line("nine slices", RenderCounter.NineSlices);
        Line("  nine slice patches", RenderCounter.NineSlicePatches);
        Line("strings", RenderCounter.Strings);
        Line("  string shadow draws", RenderCounter.StringShadowDraws);
        Line("  string outline draws", RenderCounter.StringOutlineDraws);
        Line("shapes", RenderCounter.Shapes);
        Line("lightmap light draws", RenderCounter.LightmapDraws);

        sb.AppendLine();
        sb.AppendLine($"  {"TOTAL draw calls".PadRight(NameColumn)} {stats.AvgDrawCalls,10:0.0} avg");
        sb.AppendLine($"  {"batches (sprite + shape)".PadRight(NameColumn)} {stats.AvgBatches,10:0.0} avg");

        if (stats.AvgBatches > 0)
            sb.AppendLine($"  {"draw calls per batch".PadRight(NameColumn)} {stats.AvgDrawCalls / stats.AvgBatches,10:0.0}   (higher is better)");

        var outline = stats.Get(RenderCounter.StringOutlineDraws).Avg;
        if (outline > 20)
            sb.AppendLine($"  > text outlines cost {outline:0} extra draws a frame");

        sb.AppendLine();
        Title(sb, "BATCH BREAKS");
        sb.AppendLine();

        Line("shader switch", RenderCounter.BreakShader);
        Line("sampler switch", RenderCounter.BreakSampler);
        Line("shaded/unshaded switch", RenderCounter.BreakUnshaded);
        Line("sprite -> shape", RenderCounter.BreakSpriteToShape);
        Line("shape -> sprite", RenderCounter.BreakShapeToSprite);
        sb.AppendLine($"  {"TOTAL breaks".PadRight(NameColumn)} {stats.AvgBatchBreaks,10:0.0} avg");

        WriteTransitions(sb, "top shader transitions (lifetime count)", stats.ShaderTransitions);
        WriteTransitions(sb, "top sampler transitions (lifetime count)", stats.SamplerTransitions);

        sb.AppendLine();
        Row(sb, "queue size (avg)", $"{stats.Get(RenderCounter.QueueSize).Avg:0.0}");
        Row(sb, "queue peak", stats.QueuePeak.ToString());
        Row(sb, "queue sort", $"{FormatMs(stats.SortStats.Avg)} (max {FormatMs(stats.SortStats.Max)})");
        Row(sb, "pooled entries recycled", $"{stats.Get(RenderCounter.PooledEntries).Avg:0.0}");
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
        foreach (var target in stats.Targets)
        {
            totalBytes += target.Bytes;
            sb.AppendLine(
                $"  {Truncate(target.Name, 20).PadRight(20)} {$"{target.Width}x{target.Height}",13} " +
                $"{target.Format,10} {FormatBytes(target.Bytes),10} {target.BindsHistory.Average(),8:0.0} " +
                $"{target.ClearsHistory.Average(),9:0.0} {target.Reallocs,8}");
        }

        sb.AppendLine();
        sb.AppendLine("  binds/clears are the window average; realloc is a lifetime count (a resize");
        sb.AppendLine("  is rare, a rolling average of it would just read as zero).");
        sb.AppendLine();
        Row(sb, "render target VRAM", FormatBytes(totalBytes));
        Row(sb, "target binds per frame", $"{stats.Get(RenderCounter.TargetBinds).Avg:0.0}");

        var reallocs = stats.Targets.Sum(t => t.Reallocs);
        if (reallocs > 4)
            sb.AppendLine($"  > {reallocs} render target reallocations (lifetime) - a target may be resizing every frame.");

        sb.AppendLine();
    }

    private static void WriteFill(StringBuilder sb, RenderStats stats, FrameProfiler profiler)
    {
        Title(sb, "FILL RATE (EST)");
        sb.AppendLine();

        var total = stats.TotalAvgFill;
        if (total <= 0)
        {
            sb.AppendLine("  nothing recorded.");
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

    private static void WriteDrawSystems(StringBuilder sb, RenderStats stats)
    {
        Title(sb, "DRAW SYSTEM USAGE");
        sb.AppendLine($"  {"system".PadRight(NameColumn)} {"submits/f",11} {"culled/f",10} {"cull%",8} {"extra/f",10}");

        foreach (var system in stats.DrawSystems)
        {
            var submits = system.Submits.Average();
            var culled = system.Culled.Average();
            var considered = submits + culled;
            var cullPct = considered <= 0 ? 0 : culled / considered * 100.0;

            sb.AppendLine(
                $"  {Truncate(system.Name, NameColumn).PadRight(NameColumn)} {submits,11:0.0} " +
                $"{culled,10:0.0} {cullPct,7:0.0}% {system.Extra.Average(),10:0.0}");
        }

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
        sb.AppendLine("  these are this frame's numbers, not a window average - 'total' also includes");
        sb.AppendLine("  light/occluder collection (see lighting/collect in FRAME PHASES), so a small");
        sb.AppendLine("  gap against the sum of the rows above, and against ECS SYSTEMS' windowed");
        sb.AppendLine("  'LightingSystem' average, is expected.");
        sb.AppendLine();
    }

    private static void WriteSweep(StringBuilder sb)
    {
        var sweep = GameClient.Sweep;
        if (sweep.Results.Count == 0)
            return;

        Title(sb, "PASS ISOLATION SWEEP");
        sb.AppendLine();
        sb.AppendLine($"  {"configuration".PadRight(NameColumn)} {"frame",10} {"saved",10} {"fps",8}");

        foreach (var result in sweep.Results)
        {
            var fps = result.MedianMs <= 0 ? 0 : 1000.0 / result.MedianMs;
            sb.AppendLine(
                $"  {Truncate(result.Name, NameColumn).PadRight(NameColumn)} {FormatMs(result.MedianMs),10} " +
                $"{FormatMs(result.SavedMs),10} {fps,8:0}");
        }
        sb.AppendLine();
    }

    private static void WriteSystems(StringBuilder sb, SystemsProfiler systems, FrameProfiler profiler)
    {
        Title(sb, "ECS SYSTEMS");
        sb.AppendLine($"  {"system".PadRight(NameColumn)} {"update",10} {"draw",10} {"total",10} {"upd max",10} {"alloc/f",11} {"submits",8}");

        var all = systems.GetAll().OrderByDescending(s => s.TotalMs).ToList();
        double totalUpdate = 0, totalDraw = 0;

        foreach (var system in all)
        {
            totalUpdate += system.UpdateMs;
            totalDraw += system.DrawMs;

            sb.AppendLine(
                $"  {Truncate(system.Name, NameColumn).PadRight(NameColumn)} " +
                $"{FormatMs(system.UpdateMs),10} {FormatMs(system.DrawMs),10} {FormatMs(system.TotalMs),10} " +
                $"{FormatMs(system.UpdateMaxMs),10} {FormatBytes(system.AllocBytes),11} {system.Submits,8:0}");
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
        sb.AppendLine(MemoryMeter.GetInfo());
        sb.AppendLine();
    }

    private static void WriteCensus(StringBuilder sb)
    {
        Title(sb, "CONTENT");

        var entities = GameClient.EntityManager;
        Row(sb, "entities", entities.GetEntityCount().ToString());
        Row(sb, "prototypes", GameClient.Prototypes.Count().ToString());
        Row(sb, "atlas pages", GameClient.Assets.GetAllAtlasses().Count.ToString());
        sb.AppendLine();

        var census = entities.GetComponentCensus()
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .Take(20)
            .ToList();

        if (census.Count == 0)
            return;

        sb.AppendLine("  top components:");
        foreach (var (type, count) in census)
            sb.AppendLine($"    {Truncate(type.Name, NameColumn).PadRight(NameColumn)} {count,8}");

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

    private static string FormatMs(double ms)
    {
        if (ms >= 1000.0) return $"{ms / 1000.0:0.00}s";
        if (ms >= 1.0) return $"{ms:0.00}ms";
        if (ms >= 0.001) return $"{ms * 1000.0:0.0}us";
        return "0";
    }

    private static string FormatBytes(double bytes)
    {
        var abs = Math.Abs(bytes);
        if (abs >= 1024 * 1024) return $"{bytes / 1024 / 1024:0.00}MB";
        if (abs >= 1024) return $"{bytes / 1024:0.0}KB";
        return $"{bytes:0}B";
    }

    private static string FormatTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? span.ToString(@"hh\:mm\:ss") : span.ToString(@"mm\:ss");
    }

    static string GetCpuName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string path = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue("ProcessorNameString")?.ToString() ?? "Unknown Windows CPU";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (File.Exists("/proc/cpuinfo"))
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Split(':')[1].Trim();
                    }
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return ExecuteCommand("sysctl", "-n machdep.cpu.brand_string");
        }

        return "Unknown OS / CPU";
    }

    static string ExecuteCommand(string filename, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = filename,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        using var reader = process?.StandardOutput;
        return reader?.ReadToEnd().Trim() ?? "Unknown";
    }

    #endregion
}
