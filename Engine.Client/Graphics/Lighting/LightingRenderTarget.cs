using Engine.Shared.IoC;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Owns the render target the lightmap is baked into each frame.
/// </summary>
internal sealed class LightingRenderTarget
{
    [Dependency] private readonly ViewportAdapter _viewport = default!;

    private RenderTarget2D? _target;
    private int _allocatedWidth;
    private int _allocatedHeight;

    public RenderTarget2D? Target => _target;

    internal LightingRenderTarget()
        => IoCManager.ResolveDependencies(this);

    /// <summary>
    /// Cheap to call every frame, only reallocates when the size changes.
    /// Defaults to the virtual viewport size when no size is given.
    /// </summary>
    public void EnsureSize(int w = 0, int h = 0)
    {
        if (w <= 0) w = _viewport.VirtualWidth;
        if (h <= 0) h = _viewport.VirtualHeight;

        if (w <= 0 || h <= 0)
            return;

        if (_target is not null && w == _allocatedWidth && h == _allocatedHeight)
            return;

        _target?.Dispose();

        _target = new RenderTarget2D(
            GameClient.GraphicsDevice,
            w,
            h,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            0,
            RenderTargetUsage.PreserveContents);

        _allocatedWidth = w;
        _allocatedHeight = h;

        Log.Debug($"LightingRenderTarget resized to {w}x{h}");
    }
}
