using Engine.Shared.IoC;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Owns the <see cref="RenderTarget2D"/> used to bake the lightmap each frame.
/// Sized to match the virtual viewport (1:1 with the final frame) so the
/// <c>LightingApply.fx</c> shader can sample it directly without scaling.
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
    /// Make sure the backing texture matches the requested size.
    /// Cheap to call every frame — only allocates on resize.
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
