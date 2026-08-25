using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// Tracked replacements for <see cref="GraphicsDevice.SetRenderTarget(RenderTarget2D)"/>/
/// <see cref="GraphicsDevice.Clear(Color)"/>. Every render-target switch and
/// clear in the client should go through these instead of the raw
/// GraphicsDevice call, so <see cref="RenderStats"/> sees every pass by
/// construction - a wall bleed or blur target added later can't silently end
/// up with binds/f 0.0 just because nobody remembered to add a manual
/// RecordBind call for it.
/// </summary>
public static class GraphicsDeviceProfilerExtensions
{
    public static void SetRenderTargetTracked(this GraphicsDevice device, RenderTarget2D? target, string name)
    {
        device.SetRenderTarget(target);

        var stats = GameClient.RenderStats;
        stats.TrackTarget(name, target);
        stats.RecordBind(name);
    }

    public static void ClearTracked(this GraphicsDevice device, Color color, string targetName)
    {
        device.Clear(color);
        GameClient.RenderStats.RecordClear(targetName);
    }

    public static void ClearTracked(this GraphicsDevice device, ClearOptions options, Color color, float depth, int stencil, string targetName)
    {
        device.Clear(options, color, depth, stencil);
        GameClient.RenderStats.RecordClear(targetName);
    }
}
