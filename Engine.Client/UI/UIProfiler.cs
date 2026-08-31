using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Tracks interface frame usage
/// </summary>
public static class UIProfiler
{
    private static readonly Dictionary<string, (long Ticks, int Count)> _samples = new();
    private static readonly Stopwatch _frameWatch = new();
    private static double _frameMs;
    private static int _controlCount;

    private static long _updateStart;
    private static double _updateMs;


    private static double _layoutMs;
    private static int _layoutRuns;
    private static int _frames;

    internal static void BeginFrame()
    {
        _samples.Clear();
        _controlCount = 0;
        _frameWatch.Restart();
    }

    internal static void EndFrame()
    {
        _frameWatch.Stop();
        _frameMs = _frameWatch.Elapsed.TotalMilliseconds;
    }

    internal static void BeginUpdate()
    {
        _frames++;
        _updateStart = Stopwatch.GetTimestamp();
    }

    internal static void EndUpdate()
        => _updateMs = (Stopwatch.GetTimestamp() - _updateStart) * 1000.0 / Stopwatch.Frequency;

    internal static void RecordLayout(long ticks)
    {
        _layoutRuns++;
        _layoutMs = ticks * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Record(string typeName, long ticks)
    {
        _controlCount++;
        _samples[typeName] = _samples.TryGetValue(typeName, out var existing)
            ? (existing.Ticks + ticks, existing.Count + 1)
            : (ticks, 1);
    }

    public static string LogSnapshot(bool shortVersion = false)
    {
        var sb = new StringBuilder();
        var frameBudget = 1000.0 / MathHelper.Max(1, GameClient.Timing.Fps);
        var uiTotal = _updateMs + _frameMs;

        sb.AppendLine(
            $"{GameClient.Timing.Fps}FPS ({frameBudget:0.0}ms/frame) - UI costs {uiTotal:0.000}ms, " +
            $"{uiTotal / frameBudget * 100:0.0}% of the frame");
        sb.AppendLine(
            $"  update {_updateMs:0.000}ms | draw {_frameMs:0.000}ms over {_controlCount} controls | " +
            $"layout {_layoutMs:0.000}ms, ran {_layoutRuns}/{_frames} frames");

        _layoutRuns = 0;
        _frames = 0;

        if (shortVersion)
            return sb.ToString();

        foreach (var (type, sample) in _samples.OrderByDescending(kv => kv.Value.Ticks))
        {
            var ms = sample.Ticks * 1000.0 / Stopwatch.Frequency;
            sb.AppendLine($"  {type,-20} {ms,8:0.000}ms total  ({sample.Count} instance{(sample.Count == 1 ? "" : "s")}, {ms / sample.Count:0.0000}ms avg);");
        }

        return sb.ToString();
    }
}
