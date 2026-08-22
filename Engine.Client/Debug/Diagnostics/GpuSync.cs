using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Debug.Diagnostics;

/// <summary>
/// Forces the GPU to finish everything submitted so far. Used by the profiler
/// so a render pass' timer measures the work the driver actually did instead
/// of just how long it took to queue the commands.
/// </summary>
public static class GpuSync
{
    // a 1x1 target of our own: reading back one of the real pass targets would
    // have to match its surface format (the shadow map is Single, not Color)
    // and would fail outright while that target is still bound
    private static RenderTarget2D? _probe;
    private static readonly Color[] _readback = new Color[1];

    /// <summary>
    /// Blocks until the GPU has drained. Costs a full pipeline stall - only
    /// call it from a profiler scope, never on a normal frame.
    /// </summary>
    public static void Flush()
    {
        var device = GameClient.GraphicsDevice;
        if (device is null)
            return;

        var previousTargets = device.GetRenderTargets();
        var previousViewport = device.Viewport;

        EnsureProbe(device);
        if (_probe is null)
            return;

        // the readback only returns once every command queued before it has
        // completed, so drawing into the probe and reading it drains the pipe
        device.SetRenderTarget(_probe);
        device.Clear(Color.Black);
        device.SetRenderTarget(null);
        _probe.GetData(_readback);

        RestoreTargets(device, previousTargets);
        device.Viewport = previousViewport;
    }

    private static void EnsureProbe(GraphicsDevice device)
    {
        if (_probe is { IsDisposed: false })
            return;

        _probe = new RenderTarget2D(device, 1, 1, false, SurfaceFormat.Color, DepthFormat.None);
    }

    private static void RestoreTargets(GraphicsDevice device, RenderTargetBinding[] previous)
    {
        if (previous.Length == 0)
        {
            device.SetRenderTarget(null);
            return;
        }

        device.SetRenderTargets(previous);
    }

    public static void Dispose()
    {
        _probe?.Dispose();
        _probe = null;
    }
}
