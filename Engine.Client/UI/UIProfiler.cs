using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Engine.Client.UI;

/// <summary>
/// Tracks per-control-type DrawSelf cost for the most recently completed UI frame.
/// </summary>
public static class UIProfiler
{
    private static readonly Dictionary<string, (long Ticks, int Count)> _samples = new();
    private static readonly Stopwatch _frameWatch = new();
    private static double _frameMs;
    private static int _controlCount;

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
        sb.AppendLine($"{GameClient.GameTime.Fps}FPS - {_controlCount} controls drawn, {_frameMs:0.000}ms total UI draw");

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
